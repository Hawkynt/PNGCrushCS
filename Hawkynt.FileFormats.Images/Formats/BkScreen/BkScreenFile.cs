using System;
using FileFormat.Core;

namespace FileFormat.BkScreen;

/// <summary>In-memory representation of a BK screen dump (.bks).</summary>
/// <remarks>
/// The Elektronika BK was a Soviet home computer whose display was a plain block of memory with no
/// video chip between it and the screen — so a screen dump is exactly what the machine was showing
/// and nothing else. What it was showing depends only on how much of it there is: sixteen kilobytes
/// is one screen and thirty-two is two, shown alternately.
/// <para/>
/// Monochrome is 512 pixels across at one bit each, with the bits running from the least
/// significant end of a byte rather than the most — the opposite of nearly every other machine.
/// Colour halves the resolution to use two bits a pixel, and a trailing byte says which of sixteen
/// four-colour sets they name; the sets are fixed in hardware, so a colour screen costs one byte
/// more than a monochrome one.
/// </remarks>
public readonly record struct BkScreenFile
  : IImageFormatReader<BkScreenFile>, IImageToRawImage<BkScreenFile> {

  /// <summary>Size of one screen's worth of video memory.</summary>
  public const int ScreenSize = 16384;

  /// <summary>Pixels across in monochrome.</summary>
  public const int MonochromeWidth = 512;

  /// <summary>Pixels across in colour.</summary>
  public const int ColorWidth = 256;

  /// <summary>Rows either mode shows.</summary>
  public const int Height = 512;

  /// <summary>Rows a colour screen shows, since its pixels are not stretched.</summary>
  public const int ColorHeight = 256;

  /// <summary>Colour sets the hardware offers.</summary>
  public const int PaletteCount = 16;

  /// <summary>The sixteen four-colour sets, as RGB triplets.</summary>
  public static ReadOnlySpan<byte> Palettes => [
    0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00,
    0x00, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x80, 0x00, 0x00, 0xFF, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00,
    0x00, 0x00, 0x00, 0xC0, 0x00, 0xC0, 0x80, 0x00, 0xFF, 0xFF, 0x00, 0xFF,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x80, 0x00, 0xFF, 0xC0, 0x00, 0x00,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xC0, 0x00, 0xC0, 0xFF, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0x00,
    0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF,
  ];

  static string IImageFormatMetadata<BkScreenFile>.PrimaryExtension => ".bks";
  static string[] IImageFormatMetadata<BkScreenFile>.FileExtensions => [".bks"];
  static BkScreenFile IImageFormatReader<BkScreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => BkScreenReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BkScreenFile>.VideoModes => [
    new("Monochrome", [(MonochromeWidth, Height)], [2]),
    new("Colour", [(ColorWidth, ColorHeight)], [4]),
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Whether the screen names colours rather than being monochrome.</summary>
  public bool IsColor { get; init; }

  /// <summary>Screens the file holds; two are shown on alternate fields.</summary>
  public int Frames { get; init; }

  public static RawImage ToRawImage(BkScreenFile file) {
    var data = file.Data ?? [];
    var width = file.IsColor ? ColorWidth : MonochromeWidth;
    var height = file.IsColor ? ColorHeight : Height;

    var first = _Render(data, file, width, height, 0);
    var pixels = file.Frames == 1 ? first : FrameBlend.Average(first, _Render(data, file, width, height, 1));

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Render(ReadOnlySpan<byte> data, BkScreenFile file, int width, int height, int frame) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var target = (y * width + x) * 3;

      if (file.IsColor) {
        var index = y * width + x;
        var b = _At(data, frame * ScreenSize + (index >> 2));
        var entry = (data[ScreenSize * file.Frames + frame] * 4 + ((b >> ((index & 3) << 1)) & 3)) * 3;
        rgb[target] = Palettes[entry];
        rgb[target + 1] = Palettes[entry + 1];
        rgb[target + 2] = Palettes[entry + 2];
        continue;
      }

      // Two screen rows share one stored line, and the bits run from the low end of the byte.
      var at = frame * ScreenSize + ((y >> 1) << 6) + (x >> 3);
      if (((_At(data, at) >> (x & 7)) & 1) != 0)
        rgb[target] = rgb[target + 1] = rgb[target + 2] = 255;
    }

    return rgb;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
