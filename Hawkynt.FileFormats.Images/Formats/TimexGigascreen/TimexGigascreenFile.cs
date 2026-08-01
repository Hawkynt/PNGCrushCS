using System;
using FileFormat.Core;

namespace FileFormat.TimexGigascreen;

/// <summary>In-memory representation of a Timex 2048 hi-res gigascreen picture (.hrg).</summary>
/// <remarks>
/// The Timex's hi-res mode puts 512 pixels on a line by spending both of the Spectrum's screen
/// banks on the bitmap — which leaves nothing for attributes, so the whole screen has one ink
/// colour and paper is simply its opposite. A gigascreen file holds two such screens and shows them
/// on alternate television fields, so the eye averages two two-colour pictures into rather more.
/// <para/>
/// Each screen is one byte of colour after its bitmap, and the pair sit back to back. The stored
/// 192 rows are drawn on 384 scanlines, the mode having traded vertical resolution for horizontal.
/// </remarks>
public readonly record struct TimexGigascreenFile
  : IImageFormatReader<TimexGigascreenFile>, IImageToRawImage<TimexGigascreenFile>,
    IImageFromRawImage<TimexGigascreenFile>, IImageFormatWriter<TimexGigascreenFile> {

  static byte[] IImageFormatWriter<TimexGigascreenFile>.ToBytes(TimexGigascreenFile file)
    => TimexGigascreenWriter.ToBytes(file);

  /// <summary>Displayed width.</summary>
  public const int Width = 512;

  /// <summary>Rows one screen stores.</summary>
  public const int StoredHeight = 192;

  /// <summary>Displayed height; every stored row is drawn twice.</summary>
  public const int Height = StoredHeight * 2;

  /// <summary>Bytes one screen's bitmap occupies, across both banks.</summary>
  public const int BitmapSize = 12288;

  /// <summary>Bytes one bank of a bitmap occupies.</summary>
  public const int BankSize = BitmapSize / 2;

  /// <summary>Bytes one screen occupies: its bitmap and its single colour byte.</summary>
  public const int ScreenSize = BitmapSize + 1;

  /// <summary>Offset of the second screen.</summary>
  public const int SecondScreenOffset = ScreenSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = ScreenSize * 2;

  static string IImageFormatMetadata<TimexGigascreenFile>.PrimaryExtension => ".hrg";
  static string[] IImageFormatMetadata<TimexGigascreenFile>.FileExtensions => [".hrg"];
  static TimexGigascreenFile IImageFormatReader<TimexGigascreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => TimexGigascreenReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TimexGigascreenFile>.VideoModes => [
    new("Gigascreen", [(Width, Height)], [64])
  ];

  /// <summary>The file's bytes, both screens back to back.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(TimexGigascreenFile file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(_RenderScreen(data, 0), _RenderScreen(data, SecondScreenOffset)),
    };
  }

  /// <summary>Draws one hi-res screen at the displayed size.</summary>
  private static byte[] _RenderScreen(ReadOnlySpan<byte> data, int screen) {
    // One colour for the entire screen, and paper is its exact opposite — there are no attribute
    // bytes left once both banks hold bitmap.
    var ink = _ZxColor(_At(data, screen + BitmapSize) >> 3);
    var paper = ink ^ 0xFFFFFF;

    var rgb = new byte[Width * Height * 3];
    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < Width; ++x) {
      // Bit 3 of x picks the bank; the rest addresses a byte the ZX way, eight pixels to a byte.
      var offset = screen + (x & 8) * (BankSize / 8) + ZxSpectrumGraphics.LineOffset(y) + (x >> 4);
      var color = ((_At(data, offset) >> (~x & 7)) & 1) != 0 ? ink : paper;

      // Each stored row covers two scanlines.
      for (var repeat = 0; repeat < 2; ++repeat) {
        var target = ((y * 2 + repeat) * Width + x) * 3;
        rgb[target] = (byte)(color >> 16);
        rgb[target + 1] = (byte)(color >> 8);
        rgb[target + 2] = (byte)color;
      }
    }

    return rgb;
  }

  /// <summary>The full-intensity ZX colour a three-bit value names.</summary>
  private static int _ZxColor(int value)
    => (((value >> 1) & 1) * 0xFF0000) | (((value >> 2) & 1) * 0x00FF00) | ((value & 1) * 0x0000FF);

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a picture, putting the same screen in both halves.</summary>
  /// <remarks>
  /// Both banks hold bitmap, which leaves no room for attributes at all — so a screen has one
  /// colour for the whole of it, and its paper is that colour's exact opposite. All the writer can
  /// choose is which of the eight it is, and then which side of the pair each pixel falls on.
  /// <para/>
  /// Each stored row covers two scanlines, so the picture is taken at half its displayed height.
  /// </remarks>
  public static TimexGigascreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, StoredHeight).PixelData;
    var ink = _ChooseInk(rgb);
    var screen = _PackScreen(rgb, ink);

    var data = new byte[FileSize];
    screen.CopyTo(data.AsSpan(0));
    screen.CopyTo(data.AsSpan(SecondScreenOffset));

    return new() { Data = data };
  }

  /// <summary>Which of the eight colours, paired with its opposite, suits the picture best.</summary>
  private static int _ChooseInk(ReadOnlySpan<byte> rgb) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var candidate = 0; candidate < 8; ++candidate) {
      var ink = _ZxColor(candidate);
      var paper = ink ^ 0xFFFFFF;
      long cost = 0;

      for (var at = 0; at + 2 < rgb.Length; at += 3)
        cost += Math.Min(_Distance(rgb, at, ink), _Distance(rgb, at, paper));

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>Packs one screen: the bitmap across both banks, then the colour that names both tones.</summary>
  private static byte[] _PackScreen(ReadOnlySpan<byte> rgb, int inkIndex) {
    var ink = _ZxColor(inkIndex);
    var paper = ink ^ 0xFFFFFF;
    var screen = new byte[ScreenSize];

    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 3;
      if (_Distance(rgb, at, ink) >= _Distance(rgb, at, paper))
        continue;

      var offset = (x & 8) * (BankSize / 8) + ZxSpectrumGraphics.LineOffset(y) + (x >> 4);
      if (offset < BitmapSize)
        screen[offset] |= (byte)(1 << (~x & 7));
    }

    screen[BitmapSize] = (byte)(inkIndex << 3);

    return screen;
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int at, int color) {
    long dr = rgb[at] - ((color >> 16) & 0xFF);
    long dg = rgb[at + 1] - ((color >> 8) & 0xFF);
    long db = rgb[at + 2] - (color & 0xFF);

    return dr * dr + dg * dg + db * db;
  }
}
