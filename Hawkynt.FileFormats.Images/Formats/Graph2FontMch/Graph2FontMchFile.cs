using System;
using FileFormat.Core;

namespace FileFormat.Graph2FontMch;

/// <summary>In-memory representation of a Graph2Font MCH picture (.mch).</summary>
/// <remarks>
/// A character screen in which every cell carries its own nine bytes — one of flags and eight of
/// shape — rather than pointing at a shared character set. That is the whole idea: a normal
/// character screen is cheap because cells repeat, and this format gives that up to let every cell
/// on screen be different, which is what a picture rather than a page of text needs.
/// <para/>
/// The colours are then rewritten every scanline from tables, and the sprites with them when the
/// file is long enough to carry them. A flag bit per cell can change the character's inverse
/// halfway down its own height, which doubles the colours a single cell can show.
/// </remarks>
public readonly record struct Graph2FontMchFile
  : IImageFormatReader<Graph2FontMchFile>, IImageToRawImage<Graph2FontMchFile> {

  /// <summary>Pixels across, including the borders the sprites can reach.</summary>
  public const int Width = 336;

  /// <summary>Rows.</summary>
  public const int Height = 240;

  /// <summary>Bytes a cell occupies: its flags and its eight rows.</summary>
  public const int BytesPerCell = 9;

  /// <summary>Cell rows a screen holds, which is more than it displays.</summary>
  public const int CellRows = 30;

  static string IImageFormatMetadata<Graph2FontMchFile>.PrimaryExtension => ".mch";
  static string[] IImageFormatMetadata<Graph2FontMchFile>.FileExtensions => [".mch"];
  static Graph2FontMchFile IImageFormatReader<Graph2FontMchFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graph2FontMchReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Graph2FontMchFile>.VideoModes => [
    new("Graph2Font MCH", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public int Columns { get; init; }

  /// <summary>What ANTIC is fetching, which the flags byte chooses.</summary>
  public AnticMode Mode { get; init; }

  /// <summary>The GTIA mode bits, which the same byte chooses.</summary>
  public int GtiaMode { get; init; }

  /// <summary>Whether the file carries sprites and their per-scanline tables.</summary>
  public bool HasSprites { get; init; }

  public static RawImage ToRawImage(Graph2FontMchFile file) {
    var data = file.Data ?? [];
    var bitmapLength = file.Columns * BytesPerCell * CellRows;

    // One cell anywhere with the flag set switches the whole screen to the split-inverse reading.
    var split = false;
    for (var at = 0; at < bitmapLength && at < data.Length; at += BytesPerCell)
      if ((data[at] & 64) != 0) {
        split = true;
        break;
      }

    var gtia = new _Renderer(data, split) {
      PlayfieldColumns = file.Columns,
      Priority = file.GtiaMode,
    };

    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      var colors = bitmapLength + y;
      gtia.SetTabulatedColors(data, colors, Height, file.HasSprites ? 9 : 5, file.GtiaMode);

      if (file.HasSprites) {
        for (var i = 0; i < GtiaRenderer.SpriteCount; ++i) {
          gtia.SetPlayerHpos(i, data[colors + (9 + i) * Height]);
          gtia.SetMissileHpos(i, data[colors + (13 + i) * Height]);
        }

        gtia.SetPlayerSizes(data[colors + 4080]);
        gtia.SetMissileSizes(data[colors + 4320]);
        gtia.Priority = file.GtiaMode | data[colors + 4560];
        gtia.ProcessSpriteDma(data, colors + 4800);
      }

      gtia.StartLine(44);
      gtia.DrawSpan(y, 44, 212, file.Mode, frame, Width, 0);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Every cell carries its own shape, so there is no character set to look anything up in.</summary>
  private sealed class _Renderer(byte[] data, bool split) : GtiaRenderer {

    protected override int GetPlayfieldByte(int y, int column) {
      var cell = ((y >> 3) * this.PlayfieldColumns + column) * BytesPerCell;
      if (cell + 8 >= data.Length)
        return 0;

      // A different bit of the flags byte supplies the inverse for the cell's lower half.
      var shift = split && (y & 4) != 0 ? 2 : 1;

      return ((data[cell] << shift) & 256) | data[cell + 1 + (y & 7)];
    }
  }
}
