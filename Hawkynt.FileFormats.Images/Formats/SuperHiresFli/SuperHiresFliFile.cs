using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresFli;

/// <summary>In-memory representation of a Super Hires FLI Editor picture (.shf).</summary>
/// <remarks>
/// A C64 FLI screen with sprites laid over its left twelve columns. FLI already spends the whole
/// processor rewriting the video matrix every scanline to escape the cell colour limit; the sprites
/// are there because the technique leaves the leftmost cells unusable, so the picture would
/// otherwise begin with a band of garbage.
/// <para/>
/// Which sprite covers which cell is a fixed table rather than anything the file stores — the
/// pattern comes from how the sprites had to be reused down the screen, and it is not regular.
/// </remarks>
public readonly record struct SuperHiresFliFile
  : IImageFormatReader<SuperHiresFliFile>, IImageToRawImage<SuperHiresFliFile> {

  /// <summary>Rows.</summary>
  public const int Height = 167;

  /// <summary>Pixels across the form with sprites.</summary>
  public const int WideWidth = 208;

  /// <summary>Pixels across the form without them.</summary>
  public const int NarrowWidth = 96;

  /// <summary>Size of the form that carries sprites.</summary>
  public const int WideFileSize = 15874;

  /// <summary>Size the packed form unpacks to.</summary>
  public const int UnpackedSize = 8170;

  /// <summary>Columns the sprites cover.</summary>
  public const int SpriteColumns = 12;

  /// <summary>
  /// Which of the machine's sprites covers each cell of the sprite band, four rows at a time.
  /// </summary>
  /// <remarks>
  /// A sprite is 21 rows deep and the band is 167, so the same eight sprites are reused down the
  /// screen at positions that do not divide evenly — which is why this is a table and not a
  /// formula.
  /// </remarks>
  public static ReadOnlySpan<byte> SpriteMap => [
    128, 132, 133, 137, 138, 142, 143, 147, 148, 152, 153, 157, 158, 162, 163, 167,
    168, 172, 173, 177, 178, 182, 183, 187, 188, 192, 193, 197, 198, 202, 203, 207,
    208, 212, 213, 217, 218, 222, 223, 227, 228, 232, 233, 234, 235, 236, 237, 238,
    239, 240, 241, 242, 243, 244, 245, 246, 247, 30, 46, 62, 78, 94, 110, 126,
  ];

  static string IImageFormatMetadata<SuperHiresFliFile>.PrimaryExtension => ".shf";
  static string[] IImageFormatMetadata<SuperHiresFliFile>.FileExtensions => [".shf"];
  static SuperHiresFliFile IImageFormatReader<SuperHiresFliFile>.FromSpan(ReadOnlySpan<byte> data)
    => SuperHiresFliReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SuperHiresFliFile>.VideoModes => [
    new("Super Hires FLI", [(WideWidth, Height), (NarrowWidth, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture, unpacked if it was packed.</summary>
  public byte[] Data { get; init; }

  /// <summary>Whether the file carries the sprite band.</summary>
  public bool HasSprites { get; init; }

  public static RawImage ToRawImage(SuperHiresFliFile file) {
    var data = file.Data ?? [];
    var width = file.HasSprites ? WideWidth : NarrowWidth;
    var pixels = new byte[width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)((file.HasSprites ? _WidePixel(data, x, y) : _NarrowPixel(data, x, y)) & 15);

    return new() {
      Width = width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static int _WidePixel(ReadOnlySpan<byte> data, int x, int y) {
    var column = x >> 3;
    var bit = ~x & 7;

    if (column < SpriteColumns) {
      // Two sprites overlap each cell, four table entries apart, and the nearer one wins.
      var index = ((y & 7) << 3) + column / 3;
      var offset = y % 21 * 3 + column % 3;

      if (((_At(data, 2 + (SpriteMap[index] << 6) + offset) >> bit) & 1) != 0)
        return data[1002];

      if (((_At(data, 2 + (SpriteMap[index + 4] << 6) + offset) >> bit) & 1) != 0)
        return data[1003];
    }

    // The bitmap is a scanline lower than the sprites, which is how FLI's timing lands.
    ++y;
    var at = (y & ~7) * 5 + column;

    return _At(data, 16 + ((y & 7) << 10) + at) >> (((_At(data, 8306 + (at << 3) + (y & 7)) >> bit) & 1) << 2);
  }

  private static int _NarrowPixel(ReadOnlySpan<byte> data, int x, int y) {
    var offset = y * 12 + (x >> 3);
    var bit = ~x & 7;

    if (((_At(data, offset) >> bit) & 1) != 0)
      return _At(data, 8168);

    if (((_At(data, 2048 + offset) >> bit) & 1) != 0)
      return _At(data, 8168);

    return _At(data, 6144 + offset) >> (((_At(data, 4096 + offset) >> bit) & 1) << 2);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
