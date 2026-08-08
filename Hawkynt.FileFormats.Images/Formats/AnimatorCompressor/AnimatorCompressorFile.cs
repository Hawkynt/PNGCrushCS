using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AnimatorCompressor;

/// <summary>In-memory representation of a Kompresor do Animatora sheet (.kpr).</summary>
/// <remarks>
/// An animation stored as tiles and a map, which is the compression the name refers to: consecutive
/// frames of an animation mostly repeat each other, so naming an eight-by-eight tile costs a byte
/// where drawing it costs eight. Only the tiles a frame actually uses are stored, once each.
/// <para/>
/// The frames are laid out side by side rather than one after another, so a row of the sheet runs
/// across every frame at that height — which is what makes an animation readable as a single
/// picture.
/// </remarks>
public readonly record struct AnimatorCompressorFile
  : IImageFormatReader<AnimatorCompressorFile>, IImageToRawImage<AnimatorCompressorFile>,
    IImageFromRawImage<AnimatorCompressorFile>, IImageFormatWriter<AnimatorCompressorFile> {

  /// <summary>Offset of the tile map, past the executable header and the three counts.</summary>
  public const int MapOffset = 11;

  /// <summary>Bytes a tile occupies: eight rows of four two-bit pixels.</summary>
  public const int TileLength = 8;

  /// <summary>Pixels a tile covers each way.</summary>
  public const int TileSize = 8;

  /// <summary>Tiles a map entry can name, since it is one byte.</summary>
  public const int MaxTiles = 256;

  /// <summary>The most tiles either way a count byte can state.</summary>
  public const int MaxTilesPerSide = 255;

  static string IImageFormatMetadata<AnimatorCompressorFile>.PrimaryExtension => ".kpr";
  static string[] IImageFormatMetadata<AnimatorCompressorFile>.FileExtensions => [".kpr"];
  static AnimatorCompressorFile IImageFormatReader<AnimatorCompressorFile>.FromSpan(ReadOnlySpan<byte> data)
    => AnimatorCompressorReader.FromSpan(data);
  static byte[] IImageFormatWriter<AnimatorCompressorFile>.ToBytes(AnimatorCompressorFile file)
    => AnimatorCompressorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AnimatorCompressorFile>.VideoModes => [
    new("Animation", [(IntegerRange.Any, IntegerRange.Any)], [4])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Frames the animation holds.</summary>
  public int Frames { get; init; }

  /// <summary>Tiles across one frame.</summary>
  public int Columns { get; init; }

  /// <summary>Tiles down.</summary>
  public int Rows { get; init; }

  /// <summary>Entries the tile map holds.</summary>
  public int MapLength => Frames * Columns * Rows;

  public static RawImage ToRawImage(AnimatorCompressorFile file) {
    var data = file.Data ?? [];
    var frameWidth = file.Columns << 3;
    var width = file.Frames * frameWidth;
    var height = file.Rows << 3;
    var tiles = MapOffset + file.MapLength;
    var frame = new byte[width * height];

    var target = 0;
    for (var y = 0; y < height; ++y)
    for (var f = 0; f < file.Frames; ++f)
    for (var x = 0; x < frameWidth; ++x) {
      var tile = data[MapOffset + (f * file.Rows + (y >> 3)) * file.Columns + (x >> 3)];
      var at = tiles + tile * TileLength + (y & 7);
      var pixel = at < data.Length ? (data[at] >> (~x & 6)) & 3 : 0;

      // The four values are luminances of one hue, four steps apart.
      frame[target++] = (byte)(pixel << 2);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Encodes a still picture as an animation of one frame.</summary>
  /// <remarks>
  /// The sheet lays its frames side by side and a still is one of them, so the picture handed over
  /// is the whole sheet. Padding it out with repeats would multiply the length for nothing, since
  /// every frame would be the same tiles named again.
  /// <para/>
  /// A map entry is one byte, so a picture is said in at most 256 tiles. Identical cells share one,
  /// which is the compression the name refers to, and once the set is full a cell takes whichever
  /// tile it differs from least — a limit of the format rather than of the encoder, and one a
  /// drawing rarely reaches where a photograph always does.
  /// </remarks>
  public static AnimatorCompressorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Clamp((image.Width + TileSize / 2) / TileSize, 1, MaxTilesPerSide);
    var rows = Math.Clamp((image.Height + TileSize / 2) / TileSize, 1, MaxTilesPerSide);
    var width = columns * TileSize;
    var height = rows * TileSize;
    var rgb = image.SampleTo(width, height).PixelData;

    // The four values are luminances of one hue, four steps apart, so which of them a pixel takes
    // is settled against the palette rather than by dividing its brightness.
    var levels = new byte[4];
    for (var level = 0; level < 4; ++level) {
      var entry = (level << 2) * 3;
      levels[level] = Atari8BitGraphics.Palette[entry + 1];
    }

    var map = new byte[columns * rows];
    var tiles = new List<byte[]>();

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      var tile = new byte[TileLength];
      for (var y = 0; y < TileSize; ++y) {
        var bits = 0;
        for (var pixel = 0; pixel < 4; ++pixel) {
          var at = ((row * TileSize + y) * width + column * TileSize + pixel * 2) * 3;
          var grey = (rgb[at] * 77 + rgb[at + 1] * 150 + rgb[at + 2] * 29) >> 8;

          var best = 0;
          for (var level = 1; level < 4; ++level)
            if (Math.Abs(grey - levels[level]) < Math.Abs(grey - levels[best]))
              best = level;

          bits |= best << (6 - pixel * 2);
        }

        tile[y] = (byte)bits;
      }

      map[row * columns + column] = _Intern(tiles, tile);
    }

    return new() {
      Data = AnimatorCompressorWriter.Assemble(1, columns, rows, map, tiles),
      Frames = 1,
      Columns = columns,
      Rows = rows,
    };
  }

  /// <summary>The tile's number in the set, adding it when there is room and matching it when not.</summary>
  private static byte _Intern(List<byte[]> tiles, byte[] tile) {
    for (var i = 0; i < tiles.Count; ++i)
      if (tiles[i].AsSpan().SequenceEqual(tile))
        return (byte)i;

    if (tiles.Count < MaxTiles) {
      tiles.Add(tile);
      return (byte)(tiles.Count - 1);
    }

    var best = 0;
    var bestCost = int.MaxValue;
    for (var i = 0; i < tiles.Count; ++i) {
      var cost = 0;
      for (var y = 0; y < TileLength; ++y)
      for (var pixel = 0; pixel < 4; ++pixel) {
        var shift = 6 - pixel * 2;
        var difference = ((tiles[i][y] >> shift) & 3) - ((tile[y] >> shift) & 3);
        cost += difference * difference;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return (byte)best;
  }
}
