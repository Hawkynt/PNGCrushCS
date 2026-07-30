using System;
using FileFormat.Core;

namespace FileFormat.GunPaint;

/// <summary>In-memory representation of a GunPaint picture (.gun, .ifl) for the Commodore 64.</summary>
/// <remarks>
/// Two multicolour FLI screens shown on alternate television fields, the second displaced a pixel
/// from the first. FLI means the video matrix is switched on every scanline rather than every eight,
/// so each row of a character cell gets its own pair of colours; interlacing two such screens
/// doubles that again.
/// <para/>
/// The background changes per scanline as well, and its table is in three pieces — one run for most
/// of the screen, a second for twenty lines near the bottom, and a single byte for the last three.
/// That is less a design than wherever the editor found room.
/// <para/>
/// The raster work costs the leftmost characters, which is why the picture is 296 pixels wide.
/// </remarks>
public readonly record struct GunPaintFile
  : IImageFormatReader<GunPaintFile>, IImageToRawImage<GunPaintFile> {

  /// <summary>Displayed width; the raster work costs the leftmost cells.</summary>
  public const int Width = 296;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells a stored row spans.</summary>
  public const int StrideColumns = 40;

  /// <summary>Distance between one FLI video matrix and the next.</summary>
  public const int MatrixStride = 1024;

  /// <summary>Offset of the colour RAM, which both fields share.</summary>
  public const int ColorRamOffset = 16389;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstBitmapOffset = 8194 + 24;

  /// <summary>Offset of the first field's video matrices.</summary>
  public const int FirstMatrixOffset = 2 + 3;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondBitmapOffset = 25602 + 24;

  /// <summary>Offset of the second field's video matrices.</summary>
  public const int SecondMatrixOffset = 17410 + 3;

  /// <summary>How far the second field sits from the first.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>Smallest a file can be; some carry one byte more.</summary>
  public const int FileSize = 33602;

  static string IImageFormatMetadata<GunPaintFile>.PrimaryExtension => ".gun";
  static string[] IImageFormatMetadata<GunPaintFile>.FileExtensions => [".gun", ".ifl"];
  static GunPaintFile IImageFormatReader<GunPaintFile>.FromSpan(ReadOnlySpan<byte> data) => GunPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GunPaintFile>.VideoModes => [
    new("GunPaint", [(Width, Height)], [Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>The background colour a scanline uses.</summary>
  /// <remarks>
  /// Three runs rather than one table: most of the screen, then twenty lines from a second place,
  /// then one byte serving whatever is left.
  /// </remarks>
  public static int BackgroundOffsetFor(int y) => y < 177 ? 16209 + y : y < 197 ? 18233 + y : 18429;

  public static RawImage ToRawImage(GunPaintFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _RenderField(data, FirstBitmapOffset, FirstMatrixOffset, 0, palette);
    var second = _RenderField(data, SecondBitmapOffset, SecondMatrixOffset, SecondFieldShift, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _RenderField(
    ReadOnlySpan<byte> data, int bitmap, int matrixBase, int shift, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      var background = (byte)(_At(data, BackgroundOffsetFor(y)) & 15);
      // FLI: the row within the cell picks which of the eight matrices applies.
      var matrix = matrixBase + ((y & 7) * MatrixStride);

      for (var x = 0; x < Width; ++x) {
        var source = x - shift;
        var index = source < 0 ? background : _ColorAt(data, bitmap, matrix, background, source, y);
        var entry = index * 3;
        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return rgb;
  }

  private static byte _ColorAt(ReadOnlySpan<byte> data, int bitmap, int matrix, byte background, int x, int y) {
    var cell = (y >> 3) * StrideColumns + (x >> 3);
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
