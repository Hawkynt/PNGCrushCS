using System;
using FileFormat.Core;

namespace FileFormat.ShapeTable;

/// <summary>In-memory representation of a shape table (.shp).</summary>
/// <remarks>
/// Four unrelated programs on two machines chose this extension, and nothing but the content tells
/// them apart. Two are packed C64 screens, one is an Atari screen with its colours appended, and
/// one is not a picture at all but a set of shapes stored as drawing instructions — turn, move,
/// pen up, pen down — which is why it has no size until every shape has been walked to find out how
/// big it is.
/// <para/>
/// They are all here together because the extension is shared: implementing one and calling the
/// extension covered would claim three formats for the work of one.
/// </remarks>
public readonly record struct ShapeTableFileType
  : IImageFormatReader<ShapeTableFileType>, IImageToRawImage<ShapeTableFileType> {

  /// <summary>Where Blazing Paddles' shapes were loaded, which its stored addresses are relative to.</summary>
  public const int VectorLoadAddress = 31744;

  /// <summary>Size of a Blazing Paddles shape table.</summary>
  public const int VectorFileSize = 1024;

  /// <summary>Size of an Atari Graphics 7 shape file.</summary>
  public const int AtariFileSize = 4384;

  /// <summary>Where the Atari form's bitmap starts, after a header this decoder has no use for.</summary>
  public const int AtariBitmapOffset = 528;

  /// <summary>Bytes of Atari bitmap: 96 rows of 40, and then four colour registers.</summary>
  public const int AtariBitmapSize = 3844;

  /// <summary>Size of a Loadstar screen.</summary>
  public const int LoadstarFileSize = 10018;

  static string IImageFormatMetadata<ShapeTableFileType>.PrimaryExtension => ".shp";
  static string[] IImageFormatMetadata<ShapeTableFileType>.FileExtensions => [".shp"];
  static ShapeTableFileType IImageFormatReader<ShapeTableFileType>.FromSpan(ReadOnlySpan<byte> data)
    => ShapeTableReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ShapeTableFileType>.VideoModes => [
    new("Shape table", [(IntegerRange.Any, IntegerRange.Any)], [16])
  ];

  /// <summary>The file, or the screen it unpacked to.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which of the four programs wrote it.</summary>
  public ShapeTableKind Kind { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Characters across, for the hi-res form whose width varies.</summary>
  public int Columns { get; init; }

  /// <summary>Where each shape is drawn, for the vector form.</summary>
  public (int X, int Y)[] Placements { get; init; }

  public static RawImage ToRawImage(ShapeTableFileType file) {
    var data = file.Data ?? [];

    switch (file.Kind) {
      case ShapeTableKind.C64Hires: {
        var pixels = new byte[file.Width * file.Height];
        var matrix = file.Height * file.Columns;

        for (var y = 0; y < file.Height; ++y)
        for (var x = 0; x < file.Width; ++x) {
          var offset = (y & ~7) * file.Columns + (x & ~7) + (y & 7);
          var lit = ((data[offset] >> (~x & 7)) & 1) != 0;
          var attribute = data[matrix + (offset >> 3)];
          pixels[y * file.Width + x] = (byte)(lit ? attribute >> 4 : attribute & 15);
        }

        return _Indexed(pixels, file.Width, file.Height);
      }

      case ShapeTableKind.C64Multicolor:
        return Commodore64Graphics.DecodeMulticolor(
          data, data.AsSpan(8000), data.AsSpan(9000), data[10000], 160, 200);

      case ShapeTableKind.Loadstar:
        // The same screen as a packed one, but with its planes at offsets of the program's choosing.
        return Commodore64Graphics.DecodeMulticolor(
          data.AsSpan(2), data.AsSpan(8002), data.AsSpan(9018), data[9003], 160, 200);

      case ShapeTableKind.AtariGraphics7: {
        // Four colour registers follow the bitmap, background first and then PF0, PF1 and PF2 —
        // which is the order a pixel value already names, so no remapping is needed.
        var registers = data.AsSpan(AtariBitmapOffset + AtariBitmapSize - 4, 4);
        var frame = new byte[Atari8BitGraphics.Gr7Width * (file.Height / 2)];
        var pixels = Atari8BitGraphics.UnpackGr7(data, AtariBitmapOffset, file.Height / 2);

        for (var i = 0; i < pixels.Length; ++i)
          frame[i] = (byte)(registers[pixels[i]] & 254);

        // Every stored row is drawn twice, which is what the mode's own timing does.
        var doubled = new byte[Atari8BitGraphics.Gr7Width * file.Height];
        for (var y = 0; y < file.Height; ++y)
          frame.AsSpan(y / 2 * Atari8BitGraphics.Gr7Width, Atari8BitGraphics.Gr7Width)
            .CopyTo(doubled.AsSpan(y * Atari8BitGraphics.Gr7Width));

        return new() {
          Width = Atari8BitGraphics.Gr7Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = Atari8BitGraphics.ApplyPalette(doubled),
        };
      }

      default: {
        var frame = new byte[file.Width * file.Height];
        var placements = file.Placements ?? [];

        for (var i = 0; i < placements.Length; ++i)
          ShapeTableReader.DrawVector(
            data, frame, (placements[i].Y * (file.Width >> 1) + placements[i].X) << 1, i, file.Width);

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = Atari8BitGraphics.ApplyPalette(frame),
        };
      }
    }
  }

  private static RawImage _Indexed(byte[] pixels, int width, int height) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = pixels,
    Palette = Commodore64Graphics.CreatePalette(),
    PaletteCount = Commodore64Graphics.ColorCount,
  };
}
