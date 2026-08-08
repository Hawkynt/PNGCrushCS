using System;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeLce;

/// <summary>In-memory representation of a SAM Coupe interlaced picture (.lce).</summary>
/// <remarks>
/// Two complete mode 4 screens in one file, shown on alternate television fields. Each brings its
/// own palette and its own list of line interrupts, so the pair can hold twice the colours a single
/// screen can — and because the fields alternate rather than blend, the result is displayed as
/// interleaved scanlines rather than an average.
/// <para/>
/// The second screen begins wherever the first one's interrupt list ends, which is the only thing
/// in the file that says how long either of them is.
/// </remarks>
public readonly record struct SamCoupeLceFile
  : IImageFormatReader<SamCoupeLceFile>, IImageToRawImage<SamCoupeLceFile>,
    IImageFromRawImage<SamCoupeLceFile>, IImageFormatWriter<SamCoupeLceFile> {

  /// <summary>Pixels one screen stores per row.</summary>
  public const int StoredWidth = 256;

  /// <summary>Rows one screen stores.</summary>
  public const int StoredHeight = 192;

  /// <summary>Displayed width; every stored pixel is drawn twice.</summary>
  public const int Width = StoredWidth * 2;

  /// <summary>Displayed height; the two screens interleave by scanline.</summary>
  public const int Height = StoredHeight * 2;

  /// <summary>Bytes one screen's bitmap occupies, at two pixels per byte.</summary>
  public const int BitmapSize = StoredWidth / 2 * StoredHeight;

  /// <summary>Offset of a screen's interrupt list, relative to the screen's start.</summary>
  public const int InterruptOffset = 24616;

  /// <summary>Bytes between a screen's palette and its interrupt list.</summary>
  public const int PaletteToInterruptGap = 40;

  /// <summary>Colours one screen's palette holds.</summary>
  public const int PaletteSize = 16;

  /// <summary>Bytes in one interrupt record.</summary>
  public const int InterruptRecordSize = 4;

  /// <summary>Byte that closes an interrupt list.</summary>
  public const byte InterruptTerminator = 0xFF;

  static string IImageFormatMetadata<SamCoupeLceFile>.PrimaryExtension => ".lce";
  static string[] IImageFormatMetadata<SamCoupeLceFile>.FileExtensions => [".lce"];
  static SamCoupeLceFile IImageFormatReader<SamCoupeLceFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamCoupeLceReader.FromSpan(data);
  static byte[] IImageFormatWriter<SamCoupeLceFile>.ToBytes(SamCoupeLceFile file)
    => SamCoupeLceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SamCoupeLceFile>.VideoModes => [
    new("Interlaced", [(Width, Height)], [PaletteSize * 2])
  ];

  /// <summary>The file's bytes, kept whole because both screens are addressed by absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset at which the second screen begins.</summary>
  public int SecondScreenOffset { get; init; }

  public static RawImage ToRawImage(SamCoupeLceFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[Width * Height * 3];

    // Each screen owns one parity of the display: the first the even scanlines, the second the odd.
    _RenderScreen(data, 0, rgb, 0);
    _RenderScreen(data, file.SecondScreenOffset, rgb, 1);

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Bytes one screen occupies: its bitmap, its palette, and an interrupt list of nothing.</summary>
  public const int ScreenSize = InterruptOffset + 1;

  /// <summary>Offset of a screen's palette, relative to the screen's start.</summary>
  public const int PaletteOffset = InterruptOffset - PaletteToInterruptGap;

  /// <summary>
  /// Builds an interlaced picture from any image, sampling it to the 512x384 the pair displays.
  /// </summary>
  /// <remarks>
  /// The two screens do not blend: one owns the even scanlines and the other the odd ones. So they
  /// are not two attempts at the same picture but two halves of one, and each is reduced to sixteen
  /// colours on its own — which is where the format's thirty-two come from, and why reducing the
  /// whole picture once and sharing the result would throw half of them away.
  /// <para/>
  /// Neither screen is given any line interrupts. An interrupt rewrites one palette entry part-way
  /// down the screen, which would let a screen show more than its sixteen; deciding where to put one
  /// means deciding which entry the picture can most afford to change and where, and a picture that
  /// wanted the extra colours has no way of saying so. The lists are written empty, which is a
  /// terminator and nothing else, and that is what fixes where the second screen begins.
  /// </remarks>
  public static SamCoupeLceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var sampled = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24);
    var data = new byte[ScreenSize * 2];
    data[InterruptOffset] = InterruptTerminator;
    data[ScreenSize + InterruptOffset] = InterruptTerminator;

    for (var parity = 0; parity < 2; ++parity)
      _EncodeScreen(sampled.PixelData, data, ScreenSize * parity, parity);

    return new() { Data = data, SecondScreenOffset = ScreenSize };
  }

  /// <summary>Reduces the scanlines of one parity to sixteen colours and stores them as mode 4.</summary>
  private static void _EncodeScreen(ReadOnlySpan<byte> rgb, byte[] data, int screen, int parity) {
    var field = new byte[StoredWidth * StoredHeight * 3];

    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < StoredWidth; ++x) {
      // Every stored pixel is drawn two screen pixels wide, so both have a say in its colour.
      var left = ((y * 2 + parity) * Width + x * 2) * 3;
      var target = (y * StoredWidth + x) * 3;
      for (var channel = 0; channel < 3; ++channel)
        field[target + channel] = (byte)((rgb[left + channel] + rgb[left + channel + 3]) / 2);
    }

    var source = new RawImage {
      Width = StoredWidth, Height = StoredHeight, Format = PixelFormat.Rgb24, PixelData = field,
    };
    var reduced = source.EnsureIndexedAtMost(PaletteSize);
    var palette = reduced.Palette ?? [];

    var stored = data.AsSpan(screen + PaletteOffset, PaletteSize);
    for (var i = 0; i < PaletteSize; ++i) {
      var entry = i * 3;
      stored[i] = entry + 2 < palette.Length
        ? SamCoupePalette.FromRgb(palette[entry], palette[entry + 1], palette[entry + 2])
        : (byte)0;
    }

    var indices = source.EnsureIndexed(PixelFormat.Indexed8, SamCoupePalette.ToRgbTriplets(stored)).PixelData;

    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < StoredWidth; ++x) {
      // Mode 4 is four bits a pixel, high half of a byte first.
      var index = indices[y * StoredWidth + x] & 15;
      data[screen + (y << 7) + (x >> 1)] |= (byte)((x & 1) == 0 ? index << 4 : index);
    }
  }

  /// <summary>Draws one screen onto every second scanline.</summary>
  private static void _RenderScreen(ReadOnlySpan<byte> data, int screen, byte[] rgb, int parity) {
    var palette = new int[PaletteSize];
    var paletteOffset = screen + InterruptOffset - PaletteToInterruptGap;
    for (var i = 0; i < PaletteSize; ++i)
      palette[i] = SamCoupePalette.ToRgb(_At(data, paletteOffset + i));

    var interrupt = screen + InterruptOffset;
    for (var y = 0; y < StoredHeight; ++y) {
      // Records are in line order and each names the line before the one it takes effect on.
      while (interrupt + InterruptRecordSize - 1 < data.Length && y == data[interrupt] + 1) {
        var entry = data[interrupt + 1];
        if (entry >= PaletteSize)
          break;

        palette[entry] = SamCoupePalette.ToRgb(data[interrupt + 2]);
        interrupt += InterruptRecordSize;
      }

      var row = (y * 2 + parity) * Width;
      for (var x = 0; x < StoredWidth; ++x) {
        // Mode 4 is four bits a pixel, high half of a byte first.
        var index = _At(data, screen + (y << 7) + (x >> 1));
        var color = palette[((x & 1) == 0 ? index >> 4 : index & 15)];

        // Every stored pixel is drawn two screen pixels wide.
        for (var repeat = 0; repeat < 2; ++repeat) {
          var target = (row + x * 2 + repeat) * 3;
          rgb[target] = (byte)(color >> 16);
          rgb[target + 1] = (byte)(color >> 8);
          rgb[target + 2] = (byte)color;
        }
      }
    }
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
