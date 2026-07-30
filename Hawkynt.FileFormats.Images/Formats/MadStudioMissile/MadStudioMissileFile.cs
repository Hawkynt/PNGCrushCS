using System;
using FileFormat.Core;

namespace FileFormat.MadStudioMissile;

/// <summary>In-memory representation of a Mad Studio missile (.msl).</summary>
/// <remarks>
/// The same two-bit-wide GTIA missile AtariTools-800 saves, but stored one scanline to a byte and
/// only as tall as it needs to be — the height comes first, then the colour, then the rows. A
/// missile is rarely more than a few scanlines high, so the whole file is usually under twenty
/// bytes, and the format spends its space on being able to say "six rows" rather than on packing.
/// </remarks>
public readonly record struct MadStudioMissileFile
  : IImageFormatReader<MadStudioMissileFile>, IImageToRawImage<MadStudioMissileFile>,
    IImageFromRawImage<MadStudioMissileFile>, IImageFormatWriter<MadStudioMissileFile> {

  /// <summary>Screen pixels a missile occupies: two bits, each drawn twice.</summary>
  public const int Width = 4;

  /// <summary>Tallest missile the format can hold, since two bytes precede the rows.</summary>
  public const int MaxHeight = 34;

  /// <summary>Offset of the rows, after the height and the colour.</summary>
  public const int RowOffset = 2;

  static string IImageFormatMetadata<MadStudioMissileFile>.PrimaryExtension => ".msl";
  static string[] IImageFormatMetadata<MadStudioMissileFile>.FileExtensions => [".msl"];
  static MadStudioMissileFile IImageFormatReader<MadStudioMissileFile>.FromSpan(ReadOnlySpan<byte> data)
    => MadStudioMissileReader.FromSpan(data);
  static byte[] IImageFormatWriter<MadStudioMissileFile>.ToBytes(MadStudioMissileFile file)
    => MadStudioMissileWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MadStudioMissileFile>.VideoModes => [
    new("Missile", [(Width, new IntegerRange(1, MaxHeight))], [2])
  ];

  /// <summary>The missile's GTIA colour.</summary>
  public byte Color { get; init; }

  /// <summary>The rows, two bits used in each.</summary>
  public byte[] Rows { get; init; }

  public static RawImage ToRawImage(MadStudioMissileFile file) {
    var rows = file.Rows ?? [];
    var gtia = Atari8BitGraphics.Palette;

    // Index 0 is the black the missile sits on; index 1 is its own colour.
    var palette = new byte[6];
    gtia.Slice((file.Color & 254) * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * rows.Length];
    for (var y = 0; y < rows.Length; ++y) {
      var offset = y * Width;
      if ((rows[y] & 2) != 0)
        pixels[offset] = pixels[offset + 1] = 1;
      if ((rows[y] & 1) != 0)
        pixels[offset + 2] = pixels[offset + 3] = 1;
    }

    return new() {
      Width = Width,
      Height = rows.Length,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }

  public static MadStudioMissileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height < 1 || image.Height > MaxHeight)
      throw new ArgumentException(
        $"Expected {Width}x1 to {Width}x{MaxHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.Palette;

    // The missile has one colour, so it is the average of everything that is not background.
    long red = 0, green = 0, blue = 0, lit = 0;
    for (var i = 0; i < Width * image.Height; ++i) {
      int b = bgra.PixelData[i * 4], g = bgra.PixelData[i * 4 + 1], r = bgra.PixelData[i * 4 + 2];
      if (r + g + b < 96)
        continue;

      red += r; green += g; blue += b; ++lit;
    }

    var color = lit == 0
      ? (byte)0
      : Atari8BitGraphics.FindNearestColorByte(gtia, (byte)(red / lit), (byte)(green / lit), (byte)(blue / lit));

    var rows = new byte[image.Height];
    for (var y = 0; y < rows.Length; ++y) {
      var bits = 0;
      for (var half = 0; half < 2; ++half)
        if (_IsLit(bgra.PixelData, (y * Width + half * 2) * 4, gtia, color))
          bits |= 2 >> half;

      rows[y] = (byte)bits;
    }

    return new() { Color = color, Rows = rows };
  }

  private static bool _IsLit(ReadOnlySpan<byte> bgra, int offset, ReadOnlySpan<byte> gtia, byte color) {
    int b = bgra[offset], g = bgra[offset + 1], r = bgra[offset + 2];
    var toBlack = r * r + g * g + b * b;
    int dr = gtia[color * 3] - r, dg = gtia[color * 3 + 1] - g, db = gtia[color * 3 + 2] - b;
    return dr * dr + dg * dg + db * db < toBlack;
  }
}
