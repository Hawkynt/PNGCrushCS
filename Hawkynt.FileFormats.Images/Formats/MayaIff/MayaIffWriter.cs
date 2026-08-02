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
/// What was written here before was the header and then one chunk of the whole picture, with no
/// version, no nested form, and no corners — so a reader took the first four samples as the corners
/// of a tile 65535 square and went looking for memory to hold it. It now reads the size correctly.
/// <para/>
/// KNOWN INCOMPLETE: what a tile holds between its corners and its end is not settled. A reader
/// shown this takes one plane of it and draws the picture in grey, and neither interleaving the
/// channels nor separating them, in either order, changes that. Permuting further is guesswork; the
/// answer is in the tiles of a real file, which are compressed and have to be decoded first.
/// </remarks>
public static class MayaIffWriter {

  /// <summary>Bytes a tile spends stating its own corners before its pixels.</summary>
  private const int _TileHeaderSize = 8;

  /// <summary>How wide and tall a tile is, which is what the format settled on.</summary>
  private const int _TileSize = 64;

  public static byte[] ToBytes(MayaIffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Always four channels in a tile, whether the picture had an alpha plane or not: a file the
    // program itself writes uses the four-letter tag even for opaque pictures, and a reader given
    // the three-letter one takes the single plane it finds as grey.
    const int channels = 4;
    var pixels = file.PixelData ?? [];
    var tiles = new List<byte[]>();

    for (var top = 0; top < file.Height; top += _TileSize)
    for (var left = 0; left < file.Width; left += _TileSize) {
      var right = Math.Min(left + _TileSize, file.Width) - 1;
      var bottom = Math.Min(top + _TileSize, file.Height) - 1;
      var wide = right - left + 1;
      var high = bottom - top + 1;

      var tile = new byte[8 + _TileHeaderSize + wide * high * channels];
      Encoding.ASCII.GetBytes("RGBA").CopyTo(tile, 0);
      BinaryPrimitives.WriteUInt32BigEndian(tile.AsSpan(4), (uint)(tile.Length - 8));
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(8), (ushort)left);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(10), (ushort)top);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(12), (ushort)right);
      BinaryPrimitives.WriteUInt16BigEndian(tile.AsSpan(14), (ushort)bottom);

      // A tile keeps its channels apart rather than interleaved — which is why the compressed form
      // can pack each one on its own — and it names them in the order the chunk tag reads
      // backwards: alpha first where there is one, then blue, green and red.
      var at = 16;
      var sourceChannels = file.HasAlpha ? 4 : 3;
      for (var c = 0; c < channels; ++c)
      for (var y = top; y <= bottom; ++y)
      for (var x = left; x <= right; ++x) {
        if (c >= sourceChannels) {
          // An opaque picture still needs its alpha plane written, and opaque is what it is.
          tile[at++] = 255;
          continue;
        }

        var from = (y * file.Width + x) * sourceChannels + c;
        tile[at++] = from < pixels.Length ? pixels[from] : (byte)0;
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
      // Three for colour and four when there is an alpha plane as well, which is what a file
      // written by the program itself carries.
      Flags: 3u,
      // Zero is one byte a channel; one would be two, and would send a reader looking for twice
      // the data there is.
      Bytes: 0,
      Tiles: (ushort)tiles.Count,
      Compression: 0
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
