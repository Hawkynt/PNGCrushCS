using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Tier-2 packet parser: extracts code-block data segments from JPEG 2000 tile bit-stream (ITU-T T.800 Section B.9).</summary>
internal static class Tier2Decoder {

  /// <summary>Parse all packets for a single tile, returning code-block data segments.</summary>
  public static List<CodeBlockData> ParsePackets(byte[] data, int offset, int length, TileInfo tile) {
    var result = new List<CodeBlockData>();
    var subbands = SubbandInfo.ComputeSubbands(tile.Width, tile.Height, tile.DecompLevels);
    var reader = new BitReader(data, offset, length);

    // Build tag trees and inclusion state per (component, subband, code-block)
    var numComponents = tile.ComponentCount;
    var cbW = tile.CodeBlockWidth;
    var cbH = tile.CodeBlockHeight;

    // For LRCP progression: layers -> resolution levels -> components -> precincts
    for (var layer = 0; layer < tile.Layers; ++layer)
      for (var comp = 0; comp < numComponents; ++comp)
        _ParsePacketForComponent(reader, data, tile, subbands, comp, layer, result);

    return result;
  }

  private static void _ParsePacketForComponent(
    BitReader reader,
    byte[] data,
    TileInfo tile,
    SubbandInfo[] subbands,
    int component,
    int layer,
    List<CodeBlockData> result
  ) {
    // Read empty packet bit
    var nonEmpty = reader.ReadBit();
    if (nonEmpty == 0)
      return;

    var cbW = tile.CodeBlockWidth;
    var cbH = tile.CodeBlockHeight;

    foreach (var sb in subbands) {
      if (sb.Width == 0 || sb.Height == 0)
        continue;

      sb.GetCodeBlockGrid(cbW, cbH, out var numCbX, out var numCbY);

      for (var cbY = 0; cbY < numCbY; ++cbY)
        for (var cbX = 0; cbX < numCbX; ++cbX) {
          // For the first layer, always included
          // Read number of coding passes
          var numPasses = _ReadNumCodingPasses(reader);
          if (numPasses == 0)
            continue;

          // Read zero bit-planes (simplified: read as a small integer)
          var zeroBitPlanes = 0;
          if (layer == 0)
            zeroBitPlanes = _ReadZeroBitPlanes(reader, tile.BitsPerComponent);

          // Read compressed data length
          var dataLen = _ReadDataLength(reader);
          if (dataLen <= 0)
            continue;

          // Extract the compressed data bytes
          var compData = new byte[dataLen];
          var pos = reader.Position;
          if (pos + dataLen <= data.Length)
            Array.Copy(data, pos, compData, 0, dataLen);

          // Compute actual code-block dimensions (may be smaller at edges)
          var actualW = Math.Min(cbW, sb.Width - cbX * cbW);
          var actualH = Math.Min(cbH, sb.Height - cbY * cbH);

          result.Add(new CodeBlockData {
            SubbandIndex = sb.Index + component * subbands.Length,
            CodeBlockX = cbX,
            CodeBlockY = cbY,
            NumCodingPasses = numPasses,
            ZeroBitPlanes = zeroBitPlanes,
            CompressedData = compData,
          });
        }
    }
  }

  /// <summary>Read the number of coding passes from the packet header (ITU-T T.800 Table B.4).</summary>
  private static int _ReadNumCodingPasses(BitReader reader) {
    // 0 -> 1 pass
    // 10 -> 2 passes
    // 1100 -> 3 passes
    // 1101 -> 4 passes
    // 1110 xxxx -> 5 + xxxx (5-20)
    // 1111 xxxx xxxx xxxx xxxxx -> 36 + value (36-163)
    if (reader.ReadBit() == 0)
      return 1;

    if (reader.ReadBit() == 0)
      return 2;

    var b1 = reader.ReadBit();
    var b2 = reader.ReadBit();
    if (b1 == 0)
      return b2 == 0 ? 3 : 4;

    if (b2 == 0)
      return 5 + reader.ReadBits(4);

    return 36 + reader.ReadBits(13);
  }

  /// <summary>Read zero bit-plane count as a small unary-coded value.</summary>
  private static int _ReadZeroBitPlanes(BitReader reader, int bitsPerComponent) {
    var count = 0;
    var maxPlanes = bitsPerComponent + 1;
    while (count < maxPlanes && reader.ReadBit() == 0)
      ++count;

    return count;
  }

  /// <summary>Read the data length for a code-block contribution (length prefix coding).</summary>
  private static int _ReadDataLength(BitReader reader) {
    // Read length indicator: number of extra bits as unary, then the length bits
    var lblock = 3; // Initial Lblock value
    // Read extra bits indicator
    while (reader.ReadBit() != 0)
      ++lblock;

    return reader.ReadBits(lblock);
  }
}
