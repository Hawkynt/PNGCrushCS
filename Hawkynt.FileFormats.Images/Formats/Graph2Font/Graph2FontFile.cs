using System;
using FileFormat.Core;

namespace FileFormat.Graph2Font;

/// <summary>In-memory representation of a Graph2Font picture (.g2f).</summary>
/// <remarks>
/// The editor's own project format, which is why it is a hundred and fifty kilobytes for a screen
/// of a few: it stores every register the display uses for every one of the 240 scanlines, up to
/// 128 character sets, a character set number per row, a display mode per row, and the sprites —
/// all as plain tables, because the program's job is to let each be edited independently.
/// <para/>
/// So the picture is not stored anywhere. What is stored is every input the chip needs, and the
/// only way to see the result is to run them through it.
/// </remarks>
public readonly record struct Graph2FontFile
  : IImageFormatReader<Graph2FontFile>, IImageToRawImage<Graph2FontFile>,
    IImageFromRawImage<Graph2FontFile>, IImageFormatWriter<Graph2FontFile> {

  /// <summary>Pixels across, including the borders the sprites can reach.</summary>
  public const int Width = 336;

  /// <summary>Rows one frame occupies.</summary>
  public const int Height = 240;

  /// <summary>Size of one character set.</summary>
  public const int FontSize = 1024;

  /// <summary>The text a compressed file starts with.</summary>
  public const string CompressedSignature = "G2FZLIB";

  static string IImageFormatMetadata<Graph2FontFile>.PrimaryExtension => ".g2f";
  static string[] IImageFormatMetadata<Graph2FontFile>.FileExtensions => [".g2f"];
  static Graph2FontFile IImageFormatReader<Graph2FontFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graph2FontReader.FromSpan(data);
  static byte[] IImageFormatWriter<Graph2FontFile>.ToBytes(Graph2FontFile file)
    => Graph2FontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Graph2FontFile>.VideoModes => [
    new("Graph2Font", [(Width, Height)], [256])
  ];

  /// <summary>The project, uncompressed if it was compressed.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(Graph2FontFile file) {
    var frame = new byte[Width * Height];
    Render(file.Data ?? [], frame, 0, Width);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Builds a project that draws the given picture.</summary>
  /// <remarks>
  /// Of everything the editor lets a project carry, almost none is spent: no sprites, no raster
  /// program, no video upgrade, and one display mode down the whole screen. What carries the picture
  /// is the colour table, which has an entry per scanline, and the character sets, of which a
  /// project may hold 128 — one per row of cells is enough for every cell to have a character
  /// nothing else uses.
  /// </remarks>
  public static Graph2FontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Data = Graph2FontEncoder.Encode(image.SampleTo(Width, Height).PixelData) };
  }

  /// <summary>Draws one project into a frame, which the vertical-scroll format stacks several of.</summary>
  public static void Render(ReadOnlySpan<byte> data, Span<byte> frame, int yOffset, int width) {
    var layout = Graph2FontReader.Describe(data);
    var gtia = new _Renderer(data.ToArray(), layout) { PlayfieldColumns = layout.Columns };

    for (var y = 0; y < Height; ++y) {
      var row = y >> 3;
      gtia.FontOffset = layout.FontsOffset + ((data[layout.FontNumberOffset + row] & 127) << 10);
      if (gtia.FontOffset >= layout.FontNumberOffset)
        throw new System.IO.InvalidDataException($"Row {row} names a character set the project does not hold.");

      var sprite = layout.FontNumberOffset + 2334 + (y << 1);

      // The stored priority is an index into five arrangements rather than the register's own bits.
      ReadOnlySpan<byte> priorities = [4, 2, 1, 8, 0];
      var chosen = (data[sprite + 1] >> 4) & 7;
      if (chosen >= priorities.Length)
        throw new System.IO.InvalidDataException($"Scanline {y} names no priority arrangement.");

      var priority = priorities[chosen] | (data[sprite + 1025] & 48);

      var mode = data[layout.FontNumberOffset + 153694 + row] switch {
        1 => AnticMode.HiRes,
        2 => layout.CharacterMode ? AnticMode.FiveColor : AnticMode.FourColor,
        4 => AnticMode.HiRes,
        255 => AnticMode.Blank,
        _ => throw new System.IO.InvalidDataException($"Row {row} names no display mode."),
      };

      // A row in the ninth mode picks one of the GTIA modes from the project's own header.
      if (data[layout.FontNumberOffset + 153694 + row] == 4) {
        ReadOnlySpan<byte> gtiaModes = [64, 64, 64, 64, 64, 128, 192, 64];
        priority |= gtiaModes[data[1] & 7];
      }

      var colors = layout.FontNumberOffset + 30 + y;
      gtia.SetTabulatedColors(data, colors, 256, 9, priority);

      var missiles = 0;
      for (var i = 0; i < GtiaRenderer.SpriteCount; ++i) {
        _SetSprite(gtia, data, sprite, i, false);
        _SetSprite(gtia, data, sprite + 512, i, true);
        gtia.SetPlayerGraphics(i, data[colors + 6400 + (i << 9)]);
        missiles |= (data[colors + 6656 + (i << 9)] >> 6) << (i << 1);
      }

      gtia.MissileGraphics = missiles;
      gtia.Priority = priority;
      gtia.StartLine(44);
      gtia.DrawSpan(y, 44, 212, mode, frame, width, yOffset);
    }
  }

  /// <summary>
  /// Places one sprite, whose stored width is a count of doublings and whose high bit parks it off
  /// screen entirely.
  /// </summary>
  private static void _SetSprite(GtiaRenderer gtia, ReadOnlySpan<byte> data, int offset, int index, bool missile) {
    offset += index << 10;
    var value = data[offset + 1];

    if (value >= 128) {
      if (missile)
        gtia.SetMissileHpos(index, 0);
      else
        gtia.SetPlayerHpos(index, 0);

      return;
    }

    var width = (value & 15) switch {
      0 or 1 => 1,
      2 => 2,
      4 => 4,
      _ => throw new System.IO.InvalidDataException("A Graph2Font sprite has no width the chip can make."),
    };

    if (missile) {
      gtia.SetMissileWidth(index, width);
      gtia.SetMissileHpos(index, 32 + data[offset]);
    } else {
      gtia.SetPlayerWidth(index, width);
      gtia.SetPlayerHpos(index, 32 + data[offset]);
    }
  }

  /// <summary>
  /// The playfield comes from the row's own character set, and from a second inverse table when the
  /// project splits a cell's colours half way down it.
  /// </summary>
  private sealed class _Renderer(byte[] data, Graph2FontLayout layout) : GtiaRenderer {

    /// <summary>Where the current row's character set begins.</summary>
    public int FontOffset { get; set; }

    protected override int GetPlayfieldByte(int y, int column) {
      // A project with the video upgrade colours each cell independently, per scanline group.
      if (layout.VbxeOffset >= 0) {
        var at = layout.VbxeOffset + 3
                 + ((24 - (this.PlayfieldColumns >> 1) + column) * 240 + y / data[layout.VbxeOffset + 2]) * 12 + 2;

        this.Colors[4] = data[at];
        this.Colors[5] = data[at + 2];
        this.Colors[6] = data[at + 4];
      }

      var cell = (y >> 3) * this.PlayfieldColumns + column;
      var character = data[3 + cell];
      var inverse = layout.Inverse2Offset >= 0 && (y & 4) != 0 ? data[layout.Inverse2Offset + cell] : character;

      return ((inverse & 128) << 1) | data[this.FontOffset + ((character & 127) << 3) + (y & 7)];
    }

    protected override int GetHiresColor(int color)
      => layout.VbxeOffset >= 0 ? this.Colors[5] : (color & 240) | (this.Colors[5] & 14);
  }
}
