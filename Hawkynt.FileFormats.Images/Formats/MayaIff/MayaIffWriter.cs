using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.MayaIff;

/// <summary>Assembles Maya IFF file bytes from pixel data.</summary>
/// <remarks>
/// The picture is stored as tiles, and the tiles live in a form of their own inside the outer one:
/// <c>FOR4/CIMG</c> holds a version, the header, and then a nested <c>FOR4/TBMP</c> whose chunks are
/// the tiles. Each tile states its own corners before its pixels.
/// <para/>
/// What a tile holds between its corners and its end used to be recorded here as unsettled, and
/// this wrote one plane per channel, top row first, in the order the tag reads forwards. That is
/// none of the three things the format does, and it is settled now against files another converter
/// wrote: the channels are named backwards for however many the flags say, the rows run from the
/// bottom of the tile upwards, and an uncompressed tile is interleaved rather than planar.
/// <para/>
/// The tiles are written uncompressed. The run-length coding the format also allows is read but not
/// produced: it saves space and says nothing a reader needs.
/// </remarks>
public static class MayaIffWriter {

  /// <summary>Bytes a tile spends stating its own corners before its pixels.</summary>
  private const int _TileHeaderSize = 8;

  /// <summary>How wide and tall a tile is, which is what the format settled on.</summary>
  private const int _TileSize = 64;

  public static byte[] ToBytes(MayaIffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var channels = file.HasAlpha ? 4 : 3;
    var pixels = file.PixelData ?? [];
    var tiles = new List<byte[]>();

    // The corners count rows from the bottom of the picture, so the bands are cut there too.
    for (var lower = 0; lower < file.Height; lower += _TileSize)
    for (var left = 0; left < file.Width; left += _TileSize) {
      var right = Math.Min(left + _TileSize, file.Width) - 1;
      var upper = Math.Min(lower + _TileSize, file.Height) - 1;
      var wide = right - left + 1;
      var high = upper - lower + 1;

      var tile = new byte[8 + _TileHeaderSize + wide * high * channels];
      // Always the four-letter tag, whether the picture has an alpha plane or not. That is what a
      // file written by another converter carries for a three-channel picture, and a reader given
      // the three-letter one refuses the file outright — the flags say how many planes there are
      // and the tag is only the name of the chunk.
      Encoding.ASCII.GetBytes("RGBA").CopyTo(tile, 0);
      BinaryPrimitives.WriteUInt32BigEndian(tile.AsSpan(4), (uint)(tile.Length - 8));
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(8), (ushort)left);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(10), (ushort)lower);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(12), (ushort)right);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(14), (ushort)upper);

      // Interleaved, the channels named backwards — alpha, blue, green, red — and the lowest row of
      // the tile written first.
      var at = 16;
      for (var y = 0; y < high; ++y)
      for (var x = 0; x < wide; ++x) {
        var from = ((file.Height - 1 - lower - y) * file.Width + left + x) * channels;
        for (var c = channels - 1; c >= 0; --c)
          tile[at++] = from + c < pixels.Length ? pixels[from + c] : (byte)0;
      }

      tiles.Add(tile);
    }

    var tileBytes = 0;
    foreach (var tile in tiles)
      tileBytes += tile.Length + (tile.Length & 1);

    // FVER, TBHD, and the nested form holding the tiles.
    var body = 4 + (8 + 4) + (8 + MayaIffTbhdHeader.StructSize) + (8 + 4 + tileBytes);
    var result = new byte[8 + body];
    var span = result.AsSpan();
    var offset = 0;

    Encoding.ASCII.GetBytes("FOR4").CopyTo(result, offset);
    BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 4)..], (uint)body);
    Encoding.ASCII.GetBytes("CIMG").CopyTo(result, offset + 8);
    offset += 12;

    Encoding.ASCII.GetBytes("FVER").CopyTo(result, offset);
    BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 4)..], 4);
    offset += 12;

    Encoding.ASCII.GetBytes("TBHD").CopyTo(result, offset);
    BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 4)..], MayaIffTbhdHeader.StructSize);
    offset += 8;

    new MayaIffTbhdHeader(
      Width: (uint)file.Width,
      Height: (uint)file.Height,
      Prnum: 1,
      Prden: 1,
      // One for colour and three when there is an alpha plane as well. The flags say how many
      // planes a tile carries; the tag on the tile does not, and a reader that took the tag for it
      // would look for a fourth plane in three planes' worth of bytes.
      Flags: MayaIffTbhdHeader.RgbFlag | (file.HasAlpha ? MayaIffTbhdHeader.AlphaFlag : 0),
      // Zero is one byte a channel; one would be two, and would send a reader looking for twice
      // the data there is.
      Bytes: 0,
      Tiles: (ushort)tiles.Count,
      // One names the run-length coding, which is what a tile is allowed to use and not what it has
      // to: a tile as long as its own pixels is stored as it stands, which is the choice the format
      // leaves to the writer and which both readers make by measuring rather than by this field.
      // Saying nought instead sends another reader down a path that does not draw these tiles.
      Compression: 1
    ).WriteTo(span[offset..]);
    offset += MayaIffTbhdHeader.StructSize;

    Encoding.ASCII.GetBytes("FOR4").CopyTo(result, offset);
    BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 4)..], (uint)(4 + tileBytes));
    Encoding.ASCII.GetBytes("TBMP").CopyTo(result, offset + 8);
    offset += 12;

    foreach (var tile in tiles) {
      tile.CopyTo(result, offset);
      offset += tile.Length + (tile.Length & 1);
    }

    return result;
  }
}
