using System;
using FileFormat.Core;

namespace FileFormat.Stellar;

/// <summary>In-memory representation of a Stellar picture (.stl).</summary>
/// <remarks>
/// Chunky colour on a machine that has none. The Spectrum's screen forces one ink and one paper on
/// every eight-by-eight cell; Stellar gives up resolution instead, drawing four-by-four blocks that
/// each carry their own colour outright. Two such screens are shown alternately and averaged, which
/// doubles the number of shades again.
/// <para/>
/// A byte holds two blocks' colours, three bits each, with the brightness bit shared between them.
/// The two frames interleave at byte granularity rather than being stored one after the other.
/// </remarks>
public readonly record struct StellarFile
  : IImageFormatReader<StellarFile>, IImageToRawImage<StellarFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = ZxSpectrumGraphics.ScreenWidth;

  /// <summary>Rows.</summary>
  public const int Height = ZxSpectrumGraphics.ScreenHeight;

  /// <summary>Screen pixels a block spans, both ways.</summary>
  public const int BlockSize = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = 3072;

  static string IImageFormatMetadata<StellarFile>.PrimaryExtension => ".stl";
  static string[] IImageFormatMetadata<StellarFile>.FileExtensions => [".stl"];
  static StellarFile IImageFormatReader<StellarFile>.FromSpan(ReadOnlySpan<byte> data)
    => StellarReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<StellarFile>.VideoModes => [
    new("Stellar", [(Width, Height)], [ZxSpectrumGraphics.PaletteEntryCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(StellarFile file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(_RenderField(data, 0), _RenderField(data, 1)),
    };
  }

  private static byte[] _RenderField(ReadOnlySpan<byte> data, int field) {
    var palette = ZxSpectrumGraphics.Palette;
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = ((y & ~3) << 4) | ((x >> 2) & ~3) | (field << 1) | ((x >> 3) & 1);
      var b = at < data.Length ? data[at] : 0;

      // Two blocks to a byte; the second takes the high three bits, and bit 6 brightens both.
      var color = ((x & 4) == 0 ? b >> 3 : b) & 7;
      var entry = (((b >> 6) & 1) * 8 + color) * 3;

      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }
}
