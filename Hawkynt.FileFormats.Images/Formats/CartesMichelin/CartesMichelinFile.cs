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
  : IImageFormatReader<CartesMichelinFile>, IImageToRawImage<CartesMichelinFile> {

  /// <summary>The bounds the tile size has to fall in.</summary>
  public const int MinTileSize = 32, MaxTileSize = 512;

  /// <summary>The bounds the grid counts have to fall in.</summary>
  public const int MinGridCount = 2, MaxGridCount = 64;

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

  static VideoMode[] IImageFormatMetadata<CartesMichelinFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Abstains: the four numbers at the front are a range check rather than a signature, and only
  /// walking the directory to a tile that opens <c>GIF8</c> says the file is one of these.
  /// </summary>
  static bool? IImageFormatMetadata<CartesMichelinFile>.MatchesSignature(ReadOnlySpan<byte> header) => null;

  /// <summary>Pixels across in the assembled sheet.</summary>
  public int Width { get; init; }

  /// <summary>Rows in the assembled sheet.</summary>
  public int Height { get; init; }

  /// <summary>Pixels across in one tile.</summary>
  public int TileWidth { get; init; }

  /// <summary>Rows in one tile.</summary>
  public int TileHeight { get; init; }

  /// <summary>How many tiles the sheet was assembled from.</summary>
  public int TileCount { get; init; }

  /// <summary>Three bytes a pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CartesMichelinFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };
}
