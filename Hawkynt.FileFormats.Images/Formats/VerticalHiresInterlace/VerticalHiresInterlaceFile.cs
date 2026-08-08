using System;
using FileFormat.Core;

namespace FileFormat.VerticalHiresInterlace;

/// <summary>In-memory representation of a Vertical Hires Interlace picture (.vhi) for the Commodore 64.</summary>
/// <remarks>
/// Two high-resolution bitmaps shown on alternate television fields, sharing one video matrix. That
/// sharing is the whole idea: each cell still names only two colours, but a pixel can be lit in one
/// field and not the other, so the eye sees the two mixed as well as each alone — three shades from
/// two colours, at full resolution and with no extra colour memory.
/// </remarks>
public readonly record struct VerticalHiresInterlaceFile
  : IImageFormatReader<VerticalHiresInterlaceFile>, IImageToRawImage<VerticalHiresInterlaceFile>,
    IImageFromRawImage<VerticalHiresInterlaceFile>, IImageFormatWriter<VerticalHiresInterlaceFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Character cells across a row.</summary>
  public const int Columns = Width / 8;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 17384;

  /// <summary>Size of a file that is not packed.</summary>
  public const int PlainFileSize = 17389;

  static string IImageFormatMetadata<VerticalHiresInterlaceFile>.PrimaryExtension => ".vhi";
  static string[] IImageFormatMetadata<VerticalHiresInterlaceFile>.FileExtensions => [".vhi"];
  static VerticalHiresInterlaceFile IImageFormatReader<VerticalHiresInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => VerticalHiresInterlaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<VerticalHiresInterlaceFile>.ToBytes(VerticalHiresInterlaceFile file)
    => VerticalHiresInterlaceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<VerticalHiresInterlaceFile>.VideoModes => [
    new("Vertical hires interlace", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture's bytes, packed or not, as the reader settled them.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the first field's bitmap.</summary>
  public int FirstBitmapOffset { get; init; }

  /// <summary>Offset of the second field's bitmap.</summary>
  public int SecondBitmapOffset { get; init; }

  /// <summary>Offset of the video matrix, which both fields share.</summary>
  public int VideoMatrixOffset { get; init; }

  public static RawImage ToRawImage(VerticalHiresInterlaceFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _RenderField(data, file.FirstBitmapOffset, file.VideoMatrixOffset, palette);
    var second = _RenderField(data, file.SecondBitmapOffset, file.VideoMatrixOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Builds a picture from any image, sampling it to the 320x200 screen.</summary>
  /// <remarks>
  /// The three shades are the whole point of the format, so all three are used: a pixel lit in both
  /// fields shows one of the cell's colours, lit in neither shows the other, and lit in exactly one
  /// shows their average because the two fields are averaged. So each of the 136 colour pairs is
  /// tried against a cell with every pixel free to take whichever of that pair's three shades comes
  /// closest — the pair that leaves the least total error wins, which is exact for any picture the
  /// screen can actually hold.
  /// </remarks>
  public static VerticalHiresInterlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[UnpackedSize];
    var palette = Commodore64Graphics.CreatePalette();
    Span<int> shades = stackalloc int[3];

    for (var top = 0; top < Height; top += Commodore64Graphics.CellHeight)
    for (var left = 0; left < Width; left += 8) {
      var (foreground, background) = _ChoosePair(rgb, palette, left, top);
      _Shades(palette, foreground, background, shades);

      for (var y = 0; y < Commodore64Graphics.CellHeight; ++y)
      for (var x = 0; x < 8; ++x) {
        var shade = _ClosestShade(rgb, ((top + y) * Width + left + x) * 3, shades);
        if (shade == 0)
          continue;

        // Lit in both fields is the pair's first colour, in one of them their average.
        var offset = top * Columns + left + y;
        var bit = 1 << (~x & 7);
        data[FirstPackedBitmapOffset + offset] |= (byte)bit;
        if (shade == 2)
          data[SecondPackedBitmapOffset + offset] |= (byte)bit;
      }

      data[PackedVideoMatrixOffset + (top / Commodore64Graphics.CellHeight * Columns) + left / 8]
        = (byte)((foreground << 4) | background);
    }

    return new() {
      Data = data,
      FirstBitmapOffset = FirstPackedBitmapOffset,
      SecondBitmapOffset = SecondPackedBitmapOffset,
      VideoMatrixOffset = PackedVideoMatrixOffset,
    };
  }

  /// <summary>Offset of the first field's bitmap once a file has been unpacked.</summary>
  public const int FirstPackedBitmapOffset = 0;

  /// <summary>Offset of the second field's bitmap once a file has been unpacked.</summary>
  public const int SecondPackedBitmapOffset = 8192;

  /// <summary>Offset of the shared video matrix once a file has been unpacked.</summary>
  public const int PackedVideoMatrixOffset = 16384;

  /// <summary>The three colours a pair can show, darkest first: background, average, foreground.</summary>
  private static void _Shades(ReadOnlySpan<byte> palette, int foreground, int background, Span<int> shades) {
    var high = (palette[foreground * 3] << 16) | (palette[foreground * 3 + 1] << 8) | palette[foreground * 3 + 2];
    var low = (palette[background * 3] << 16) | (palette[background * 3 + 1] << 8) | palette[background * 3 + 2];

    shades[0] = low;
    shades[1] = ((low & high) + (((low ^ high) >> 1) & 0x7F7F7F)) & 0xFFFFFF;
    shades[2] = high;
  }

  /// <summary>Which of a pair's three shades a pixel comes closest to.</summary>
  private static int _ClosestShade(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<int> shades) {
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var shade = 0; shade < shades.Length; ++shade) {
      var distance = _Distance(rgb, at, shades[shade]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = shade;
    }

    return best;
  }

  /// <summary>The pair of colours whose three shades describe a cell with the least total error.</summary>
  private static (int Foreground, int Background) _ChoosePair(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int top) {
    int bestForeground = 0, bestBackground = 0;
    var bestError = long.MaxValue;
    Span<int> shades = stackalloc int[3];

    for (var first = 0; first < Commodore64Graphics.ColorCount; ++first)
    for (var second = 0; second <= first; ++second) {
      _Shades(palette, first, second, shades);

      long error = 0;
      for (var y = 0; y < Commodore64Graphics.CellHeight; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((top + y) * Width + left + x) * 3;
        var closest = int.MaxValue;
        foreach (var shade in shades)
          closest = Math.Min(closest, _Distance(rgb, at, shade));

        error += closest;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestForeground = first;
      bestBackground = second;
    }

    return (bestForeground, bestBackground);
  }

  /// <summary>Squared distance between a pixel and a packed colour.</summary>
  private static int _Distance(ReadOnlySpan<byte> rgb, int at, int color) {
    int dr = rgb[at] - ((color >> 16) & 0xFF);
    int dg = rgb[at + 1] - ((color >> 8) & 0xFF);
    int db = rgb[at + 2] - (color & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  private static byte[] _RenderField(ReadOnlySpan<byte> data, int bitmap, int matrix, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // The C64 cell layout: a cell's eight rows are consecutive bytes.
      var offset = (y & ~7) * Columns + (x & ~7) + (y & 7);
      var set = (_At(data, bitmap + offset) >> (~x & 7)) & 1;
      var attribute = _At(data, matrix + (offset >> 3));
      var index = (attribute >> (set << 2)) & 15;

      var entry = index * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
