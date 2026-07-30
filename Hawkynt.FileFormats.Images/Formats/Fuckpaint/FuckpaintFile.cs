using System;
using FileFormat.Core;

namespace FileFormat.Fuckpaint;

/// <summary>In-memory representation of a Fuckpaint picture (.fp) for the Commodore 64.</summary>
/// <remarks>
/// Two multicolour screens shown on alternate television fields, with the second displaced one
/// pixel left of the first. The displacement is the point: two screens in register would average
/// into a duller version of themselves, whereas offsetting them lets each pixel pair with a
/// different neighbour and produces colours the VIC-II has no register for.
/// <para/>
/// The two screens share one colour RAM and one background, so what differs between the fields is
/// only the bitmap and the video matrix.
/// </remarks>
public readonly record struct FuckpaintFile
  : IImageFormatReader<FuckpaintFile>, IImageToRawImage<FuckpaintFile> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells across a row.</summary>
  public const int Columns = Width / 8;

  /// <summary>Offset of the colour RAM, which both fields share.</summary>
  public const int ColorRamOffset = 2;

  /// <summary>Offset of the first field's video matrix.</summary>
  public const int FirstMatrixOffset = 1026;

  /// <summary>Offset of the second field's video matrix.</summary>
  public const int SecondMatrixOffset = 2050;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstBitmapOffset = 3074;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondBitmapOffset = 11266;

  /// <summary>Offset of the background colour, which both fields share.</summary>
  public const int BackgroundOffset = 11074;

  /// <summary>How far left the second field sits.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = 19266;

  static string IImageFormatMetadata<FuckpaintFile>.PrimaryExtension => ".fp";
  static string[] IImageFormatMetadata<FuckpaintFile>.FileExtensions => [".fp"];
  static FuckpaintFile IImageFormatReader<FuckpaintFile>.FromSpan(ReadOnlySpan<byte> data) => FuckpaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<FuckpaintFile>.VideoModes => [
    new("Fuckpaint", [(Width, Height)], [Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(FuckpaintFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();
    var background = (byte)(_At(data, BackgroundOffset) & 15);

    var first = _RenderField(data, FirstBitmapOffset, FirstMatrixOffset, background, 0, palette);
    var second = _RenderField(data, SecondBitmapOffset, SecondMatrixOffset, background, SecondFieldShift, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Draws one field, optionally displaced left.</summary>
  private static byte[] _RenderField(
    ReadOnlySpan<byte> data, int bitmap, int matrix, byte background, int shift, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var source = x - shift;

      // Displacing the field leaves its leftmost column with nothing to show but the background.
      var index = source < 0 ? background : _ColorAt(data, bitmap, matrix, background, source, y);
      var entry = index * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  /// <summary>The palette entry a multicolour pixel draws from.</summary>
  private static byte _ColorAt(ReadOnlySpan<byte> data, int bitmap, int matrix, byte background, int x, int y) {
    var cell = (y >> 3) * Columns + (x >> 3);
    var pattern = (_At(data, bitmap + (cell << 3) + (y & 7)) >> (~x & 6)) & 3;

    return (byte)(pattern switch {
      1 => _At(data, matrix + cell) >> 4,
      2 => _At(data, matrix + cell) & 15,
      3 => _At(data, ColorRamOffset + cell) & 15,
      _ => background,
    });
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
