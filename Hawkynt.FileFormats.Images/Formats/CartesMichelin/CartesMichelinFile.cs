using System;
using FileFormat.Core;

namespace FileFormat.CartesMichelin;

/// <summary>In-memory representation of a Cartes Michelin road atlas sheet (.big).</summary>
/// <remarks>
/// The file opens with four little-endian longs and no signature at all: a tile width and a tile height,
/// each between 32 and 512, then a count of tiles across and down, each between 2 and 64. That much was
/// known before, and on its own it is not enough to claim a name with — any file whose first sixteen
/// bytes fell in those ranges would be drawn.
/// <para/>
/// What settles it is what comes next. A directory of two longs a tile follows at offset 16, one entry
/// per grid position in reading order, giving an offset and a length; a length of zero means the tile
/// is absent. Every tile that is present is a whole GIF file, and XnView checks for <c>GIF8</c> at each
/// one — so the format does have a signature, one per tile, and a file that carries no tile at all is
/// refused. That is the identification this reader uses.
/// <para/>
/// The sheet's size is the occupied part of the grid: the tile size times the width and height of the
/// bounding box of the present tiles, with that box's top-left corner drawn at the origin. All of this
/// was confirmed by construction against the converter, both for a full grid and for grids with only
/// one column and only one tile occupied.
/// </remarks>
public readonly record struct CartesMichelinFile
  : IImageFormatReader<CartesMichelinFile>, IImageToRawImage<CartesMichelinFile>,
    IImageFromRawImage<CartesMichelinFile>, IImageFormatWriter<CartesMichelinFile> {

  /// <summary>The bounds the tile size has to fall in.</summary>
  public const int MinTileSize = 32, MaxTileSize = 512;

  /// <summary>The bounds the directory's grid counts have to fall in.</summary>
  public const int MinGridCount = 2, MaxGridCount = 64;

  /// <summary>The bounds one occupied image axis can have.</summary>
  public const int MinImageSize = MinTileSize, MaxImageSize = MaxTileSize * MaxGridCount;

  /// <summary>How long the four longs are, which is where the tile directory begins.</summary>
  public const int HeaderSize = 16;

  /// <summary>How long one entry in the tile directory is.</summary>
  public const int DirectoryEntrySize = 8;

  /// <summary>The four characters every tile begins with.</summary>
  public static ReadOnlySpan<byte> TileSignature => "GIF8"u8;

  static string IImageFormatMetadata<CartesMichelinFile>.PrimaryExtension => ".big";
  static string[] IImageFormatMetadata<CartesMichelinFile>.FileExtensions => [".big"];
  static CartesMichelinFile IImageFormatReader<CartesMichelinFile>.FromSpan(ReadOnlySpan<byte> data)
    => CartesMichelinReader.FromSpan(data);
  static byte[] IImageFormatWriter<CartesMichelinFile>.ToBytes(CartesMichelinFile file)
    => CartesMichelinWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<CartesMichelinFile>.VideoModes => [
    new("Default", [(
      new IntegerRange(MinImageSize, MaxImageSize),
      new IntegerRange(MinImageSize, MaxImageSize)
    )], [16777216])
  ];

  /// <summary>
  /// Recognises the format once enough of the directory and at least one present tile's <c>GIF8</c>
  /// signature are available; otherwise it abstains rather than guessing from four numbers in range.
  /// </summary>
  static bool? IImageFormatMetadata<CartesMichelinFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => CartesMichelinReader.MatchesSignature(header);

  /// <summary>Pixels across in the assembled sheet.</summary>
  public int Width { get; init; }

  /// <summary>Rows in the assembled sheet.</summary>
  public int Height { get; init; }

  /// <summary>Pixels across in one tile.</summary>
  public int TileWidth { get; init; }

  /// <summary>Rows in one tile.</summary>
  public int TileHeight { get; init; }

  /// <summary>Columns in the file's tile directory.</summary>
  public int GridColumns { get; init; }

  /// <summary>Rows in the file's tile directory.</summary>
  public int GridRows { get; init; }

  /// <summary>How many directory positions actually carried a tile.</summary>
  public int TileCount { get; init; }

  /// <summary>Three bytes a pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CartesMichelinFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No Cartes Michelin picture was read.");
    if (file.Width is < MinImageSize or > MaxImageSize || file.Height is < MinImageSize or > MaxImageSize)
      throw new InvalidOperationException($"A Cartes Michelin picture of {file.Width}x{file.Height} is outside the format's supported bounds.");

    var required = (long)file.Width * file.Height * 3;
    if (required > file.PixelData.Length)
      throw new InvalidOperationException("The Cartes Michelin picture does not contain enough RGB pixel data for its dimensions.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..(int)required],
    };
  }

  public static CartesMichelinFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    if (!TryGetAxisLayout(image.Width, out var tileWidth, out var occupiedColumns)
        || !TryGetAxisLayout(image.Height, out var tileHeight, out var occupiedRows))
      throw new ArgumentOutOfRangeException(nameof(image),
        $"Cartes Michelin dimensions must be exactly representable by 1..{MaxGridCount} tiles of {MinTileSize}..{MaxTileSize} pixels per axis.");

    var required = (long)image.Width * image.Height * 3;
    if (required > Array.MaxLength || image.PixelData == null || image.PixelData.Length < required)
      throw new ArgumentException("The raw image does not contain enough RGB pixel data for its dimensions.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      TileWidth = tileWidth,
      TileHeight = tileHeight,
      GridColumns = Math.Max(MinGridCount, occupiedColumns),
      GridRows = Math.Max(MinGridCount, occupiedRows),
      TileCount = checked(occupiedColumns * occupiedRows),
      PixelData = image.PixelData[..(int)required],
    };
  }

  /// <summary>
  /// Finds an exact axis decomposition, preferring the fewest occupied tiles. The directory itself
  /// still has at least two positions on an axis; a one-tile image simply leaves the extra positions absent.
  /// </summary>
  internal static bool TryGetAxisLayout(int length, out int tileSize, out int occupiedCount) {
    tileSize = occupiedCount = 0;
    if (length is < MinImageSize or > MaxImageSize)
      return false;

    var firstCount = Math.Max(1, (length + MaxTileSize - 1) / MaxTileSize);
    var lastCount = Math.Min(MaxGridCount, length / MinTileSize);
    for (var count = firstCount; count <= lastCount; ++count) {
      if (length % count != 0)
        continue;

      tileSize = length / count;
      occupiedCount = count;
      return true;
    }

    return false;
  }
}
