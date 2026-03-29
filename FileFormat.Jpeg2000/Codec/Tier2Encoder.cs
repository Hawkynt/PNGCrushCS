using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Tier-2 packet assembler: writes code-block data into JPEG 2000 tile bit-stream (ITU-T T.800 Section B.9).</summary>
internal static class Tier2Encoder {

  /// <summary>Assemble all packets for a single tile from code-block data.</summary>
  public static byte[] AssemblePackets(List<CodeBlockData> codeBlocks, TileInfo tile) {
    var writer = new BitWriter();
    var subbands = SubbandInfo.ComputeSubbands(tile.Width, tile.Height, tile.DecompLevels);
    var cbW = tile.CodeBlockWidth;
    var cbH = tile.CodeBlockHeight;

    // Index code-blocks by (subbandIndex, cbX, cbY)
    var cbMap = new Dictionary<(int, int, int), CodeBlockData>();
    foreach (var cb in codeBlocks)
      cbMap[(cb.SubbandIndex, cb.CodeBlockX, cb.CodeBlockY)] = cb;

    // For LRCP progression: single layer
    for (var layer = 0; layer < tile.Layers; ++layer)
      for (var comp = 0; comp < tile.ComponentCount; ++comp) {
        // Non-empty packet bit
        writer.WriteBit(1);

        foreach (var sb in subbands) {
          if (sb.Width == 0 || sb.Height == 0)
            continue;

          sb.GetCodeBlockGrid(cbW, cbH, out var numCbX, out var numCbY);

          for (var cbY = 0; cbY < numCbY; ++cbY)
            for (var cbX = 0; cbX < numCbX; ++cbX) {
              var key = (sb.Index + comp * subbands.Length, cbX, cbY);
              if (!cbMap.TryGetValue(key, out var cb) || cb.CompressedData.Length == 0) {
                // Write zero coding passes
                _WriteNumCodingPasses(writer, 0);
                continue;
              }

              _WriteNumCodingPasses(writer, cb.NumCodingPasses);

              if (layer == 0)
                _WriteZeroBitPlanes(writer, cb.ZeroBitPlanes);

              _WriteDataLength(writer, cb.CompressedData.Length);
            }
        }
      }

    // Flush the header bits
    var headerBytes = writer.Flush();

    // Now concatenate header bytes + all code-block data in order
    var bodyParts = new List<byte[]> { headerBytes };
    for (var comp = 0; comp < tile.ComponentCount; ++comp)
      foreach (var sb in subbands) {
        if (sb.Width == 0 || sb.Height == 0)
          continue;

        sb.GetCodeBlockGrid(cbW, cbH, out var numCbX, out var numCbY);

        for (var cbY = 0; cbY < numCbY; ++cbY)
          for (var cbX = 0; cbX < numCbX; ++cbX) {
            var key = (sb.Index + comp * subbands.Length, cbX, cbY);
            if (cbMap.TryGetValue(key, out var cb) && cb.CompressedData.Length > 0)
              bodyParts.Add(cb.CompressedData);
          }
      }

    // Compute total length and build output
    var totalLen = 0;
    foreach (var part in bodyParts)
      totalLen += part.Length;

    var result = new byte[totalLen];
    var offset = 0;
    foreach (var part in bodyParts) {
      part.CopyTo(result.AsSpan(offset));
      offset += part.Length;
    }

    return result;
  }

  /// <summary>Write the number of coding passes (ITU-T T.800 Table B.4).</summary>
  private static void _WriteNumCodingPasses(BitWriter writer, int numPasses) {
    switch (numPasses) {
      case 0:
      case 1:
        writer.WriteBit(0);
        break;
      case 2:
        writer.WriteBit(1);
        writer.WriteBit(0);
        break;
      case 3:
        writer.WriteBits(0b1100, 4);
        break;
      case 4:
        writer.WriteBits(0b1101, 4);
        break;
      case >= 5 and <= 20:
        writer.WriteBits(0b1110, 4);
        writer.WriteBits(numPasses - 5, 4);
        break;
      default:
        writer.WriteBits(0b1111, 4);
        writer.WriteBits(numPasses - 36, 13);
        break;
    }
  }

  /// <summary>Write zero bit-plane count as unary-coded value.</summary>
  private static void _WriteZeroBitPlanes(BitWriter writer, int count) {
    for (var i = 0; i < count; ++i)
      writer.WriteBit(0);
    writer.WriteBit(1);
  }

  /// <summary>Write data length with length indicator prefix.</summary>
  private static void _WriteDataLength(BitWriter writer, int dataLength) {
    // Determine how many bits needed
    var lblock = 3;
    while (dataLength >= (1 << lblock))
      ++lblock;

    // Write extra bits indicator (lblock - 3 ones followed by a zero)
    var extra = lblock - 3;
    for (var i = 0; i < extra; ++i)
      writer.WriteBit(1);
    writer.WriteBit(0);

    // Write the length value
    writer.WriteBits(dataLength, lblock);
  }
}
