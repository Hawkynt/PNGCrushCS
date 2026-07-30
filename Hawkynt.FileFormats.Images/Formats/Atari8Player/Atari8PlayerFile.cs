using System;
using FileFormat.Core;

namespace FileFormat.Atari8Player;

/// <summary>In-memory representation of an AtariTools-800 player (.pla).</summary>
/// <remarks>
/// One sprite, saved on its own: a colour byte and then the 240 scanlines of its shape, eight bits
/// to a line. Nothing else — a player has no position of its own until a program gives it one, so
/// what the file holds is a shape and a colour and nothing about where it goes.
/// <para/>
/// Each bit is drawn two screen pixels wide, which is what makes a sixteen-pixel column out of an
/// eight-bit register.
/// </remarks>
public readonly record struct Atari8PlayerFile
  : IImageFormatReader<Atari8PlayerFile>, IImageToRawImage<Atari8PlayerFile>,
    IImageFromRawImage<Atari8PlayerFile>, IImageFormatWriter<Atari8PlayerFile> {

  /// <summary>Scanlines a player spans.</summary>
  public const int Height = 240;

  /// <summary>Screen pixels a player occupies: eight bits, each drawn twice.</summary>
  public const int Width = 16;

  /// <summary>Offset of the shape data, after the colour byte.</summary>
  public const int ShapeOffset = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = ShapeOffset + Height;

  static string IImageFormatMetadata<Atari8PlayerFile>.PrimaryExtension => ".pla";
  static string[] IImageFormatMetadata<Atari8PlayerFile>.FileExtensions => [".pla"];
  static Atari8PlayerFile IImageFormatReader<Atari8PlayerFile>.FromSpan(ReadOnlySpan<byte> data)
    => Atari8PlayerReader.FromSpan(data);
  static byte[] IImageFormatWriter<Atari8PlayerFile>.ToBytes(Atari8PlayerFile file)
    => Atari8PlayerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Atari8PlayerFile>.VideoModes => [
    new("Player", [(Width, Height)], [2])
  ];

  /// <summary>The player's GTIA colour.</summary>
  public byte Color { get; init; }

  /// <summary>The shape, one byte per scanline.</summary>
  public byte[] Shape { get; init; }

  public static RawImage ToRawImage(Atari8PlayerFile file) {
    var shape = file.Shape ?? [];
    var gtia = Atari8BitGraphics.Palette;

    // Index 0 is the black the sprite sits on; index 1 is the sprite's own colour.
    var palette = new byte[6];
    gtia.Slice((file.Color & 254) * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y) {
      var bits = y < shape.Length ? shape[y] : 0;
      for (var bit = 0; bit < 8; ++bit) {
        if (((bits >> (7 - bit)) & 1) == 0)
          continue;

        var offset = y * Width + bit * 2;
        pixels[offset] = pixels[offset + 1] = 1;
      }
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

  public static Atari8PlayerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.Palette;
    var shape = new byte[Height];

    // The sprite has one colour, so it is the average of everything that is not background, and a
    // pixel is part of the shape when it is nearer that than black.
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

    for (var y = 0; y < Height; ++y) {
      var bits = 0;
      for (var bit = 0; bit < 8; ++bit) {
        var pixel = (y * Width + bit * 2) * 4;
        int b = bgra.PixelData[pixel], g = bgra.PixelData[pixel + 1], r = bgra.PixelData[pixel + 2];
        var toBlack = r * r + g * g + b * b;
        int dr = gtia[color * 3] - r, dg = gtia[color * 3 + 1] - g, db = gtia[color * 3 + 2] - b;
        if (dr * dr + dg * dg + db * db < toBlack)
          bits |= 0x80 >> bit;
      }

      shape[y] = (byte)bits;
    }

    return new() { Color = color, Shape = shape };
  }
}
