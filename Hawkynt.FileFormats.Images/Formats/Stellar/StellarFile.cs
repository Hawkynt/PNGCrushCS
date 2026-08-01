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
  : IImageFormatReader<StellarFile>, IImageToRawImage<StellarFile>,
    IImageFromRawImage<StellarFile>, IImageFormatWriter<StellarFile> {

  static byte[] IImageFormatWriter<StellarFile>.ToBytes(StellarFile file) => StellarWriter.ToBytes(file);

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

  /// <summary>Builds a picture, putting the same field in both halves.</summary>
  /// <remarks>
  /// There is no bitmap here at all — the picture is nothing but blocks of flat colour, two to a
  /// byte, sharing one brightness bit between them. So a pair of neighbouring blocks has to agree on
  /// whether it is bright, and the two are chosen together rather than one at a time.
  /// </remarks>
  public static StellarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var data = new byte[FileSize];

    for (var field = 0; field < 2; ++field)
    for (var y = 0; y < Height; y += BlockSize)
    for (var x = 0; x < Width; x += BlockSize * 2) {
      var at = ((y & ~3) << 4) | ((x >> 2) & ~3) | (field << 1) | ((x >> 3) & 1);
      if (at >= data.Length)
        continue;

      data[at] = _EncodePair(rgb, x, y);
    }

    return new() { Data = data };
  }

  /// <summary>Chooses the two colours of one byte, and the brightness they have to share.</summary>
  private static byte _EncodePair(ReadOnlySpan<byte> rgb, int x, int y) {
    var left = _Average(rgb, x, y);
    var right = _Average(rgb, x + BlockSize, y);

    var best = 0;
    var bestCost = long.MaxValue;

    for (var bright = 0; bright < 2; ++bright) {
      var (leftColor, leftCost) = _Nearest(left, bright);
      var (rightColor, rightCost) = _Nearest(right, bright);
      var cost = leftCost + rightCost;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (bright << 6) | (leftColor << 3) | rightColor;
    }

    return (byte)best;
  }

  /// <summary>The mean colour of one block.</summary>
  private static (int Red, int Green, int Blue) _Average(ReadOnlySpan<byte> rgb, int x, int y) {
    int red = 0, green = 0, blue = 0, count = 0;

    for (var row = y; row < y + BlockSize && row < Height; ++row)
    for (var column = x; column < x + BlockSize && column < Width; ++column) {
      var at = (row * Width + column) * 3;
      if (at + 2 >= rgb.Length)
        continue;

      red += rgb[at];
      green += rgb[at + 1];
      blue += rgb[at + 2];
      ++count;
    }

    return count == 0 ? (0, 0, 0) : (red / count, green / count, blue / count);
  }

  /// <summary>The nearest of the eight colours at one brightness, and how far off it is.</summary>
  private static (int Color, long Cost) _Nearest((int Red, int Green, int Blue) color, int bright) {
    var palette = ZxSpectrumGraphics.Palette;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var candidate = 0; candidate < 8; ++candidate) {
      var entry = (bright * 8 + candidate) * 3;
      long dr = color.Red - palette[entry], dg = color.Green - palette[entry + 1], db = color.Blue - palette[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return (best, bestCost);
  }
}
