using System;
using FileFormat.Core;

namespace FileFormat.Atari8Missile;

/// <summary>In-memory representation of an AtariTools-800 missile (.mis).</summary>
/// <remarks>
/// A missile is the narrowest thing the GTIA draws: two bits wide against the whole 240-scanline
/// height of the display. Four scanlines therefore fit in one byte, most significant pair first,
/// and the shape of a missile costs sixty bytes where a player's costs two hundred and forty.
/// <para/>
/// Each bit is drawn two screen pixels wide, so the two-pixel missile occupies four across.
/// </remarks>
public readonly record struct Atari8MissileFile
  : IImageFormatReader<Atari8MissileFile>, IImageToRawImage<Atari8MissileFile>,
    IImageFromRawImage<Atari8MissileFile>, IImageFormatWriter<Atari8MissileFile> {

  /// <summary>Scanlines a missile spans.</summary>
  public const int Height = 240;

  /// <summary>Screen pixels a missile occupies: two bits, each drawn twice.</summary>
  public const int Width = 4;

  /// <summary>Scanlines packed into one shape byte.</summary>
  public const int RowsPerByte = 4;

  /// <summary>Offset of the shape data, after the colour byte.</summary>
  public const int ShapeOffset = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = ShapeOffset + Height / RowsPerByte;

  /// <summary>Size of the long form, which carries the shape padded out to a byte per scanline.</summary>
  public const int PaddedFileSize = ShapeOffset + Height;

  static string IImageFormatMetadata<Atari8MissileFile>.PrimaryExtension => ".mis";
  static string[] IImageFormatMetadata<Atari8MissileFile>.FileExtensions => [".mis"];
  static Atari8MissileFile IImageFormatReader<Atari8MissileFile>.FromSpan(ReadOnlySpan<byte> data)
    => Atari8MissileReader.FromSpan(data);
  static byte[] IImageFormatWriter<Atari8MissileFile>.ToBytes(Atari8MissileFile file)
    => Atari8MissileWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Atari8MissileFile>.VideoModes => [
    new("Missile", [(Width, Height)], [2])
  ];

  /// <summary>The missile's GTIA colour.</summary>
  public byte Color { get; init; }

  /// <summary>The shape, four scanlines to a byte.</summary>
  public byte[] Shape { get; init; }

  /// <summary>Whether the file carried the padded long form, so writing reproduces it.</summary>
  public bool IsPadded { get; init; }

  public static RawImage ToRawImage(Atari8MissileFile file) {
    var shape = file.Shape ?? [];
    var gtia = Atari8BitGraphics.Palette;

    // Index 0 is the black the missile sits on; index 1 is its own colour.
    var palette = new byte[6];
    gtia.Slice((file.Color & 254) * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y) {
      var index = y / RowsPerByte;
      var bits = index < shape.Length ? shape[index] >> ((~y & 3) << 1) : 0;
      var offset = y * Width;

      if ((bits & 2) != 0)
        pixels[offset] = pixels[offset + 1] = 1;
      if ((bits & 1) != 0)
        pixels[offset + 2] = pixels[offset + 3] = 1;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }

  public static Atari8MissileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.Palette;

    // The missile has one colour, so it is the average of everything that is not background, and a
    // pixel belongs to the shape when it is nearer that than black.
    long red = 0, green = 0, blue = 0, lit = 0;
    for (var i = 0; i < Width * Height; ++i) {
      int b = bgra.PixelData[i * 4], g = bgra.PixelData[i * 4 + 1], r = bgra.PixelData[i * 4 + 2];
      if (r + g + b < 96)
        continue;

      red += r; green += g; blue += b; ++lit;
    }

    var color = lit == 0
      ? (byte)0
      : Atari8BitGraphics.FindNearestColorByte(gtia, (byte)(red / lit), (byte)(green / lit), (byte)(blue / lit));

    var shape = new byte[Height / RowsPerByte];
    for (var y = 0; y < Height; ++y) {
      var bits = 0;
      for (var half = 0; half < 2; ++half)
        if (_IsLit(bgra.PixelData, (y * Width + half * 2) * 4, gtia, color))
          bits |= 2 >> half;

      shape[y / RowsPerByte] |= (byte)(bits << ((~y & 3) << 1));
    }

    return new() { Color = color, Shape = shape };
  }

  private static bool _IsLit(ReadOnlySpan<byte> bgra, int offset, ReadOnlySpan<byte> gtia, byte color) {
    int b = bgra[offset], g = bgra[offset + 1], r = bgra[offset + 2];
    var toBlack = r * r + g * g + b * b;
    int dr = gtia[color * 3] - r, dg = gtia[color * 3 + 1] - g, db = gtia[color * 3 + 2] - b;
    return dr * dr + dg * dg + db * db < toBlack;
  }
}
