using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>JPEG 2000 Tier-2 packet assembler (ITU-T T.800 B.9-B.12).</summary>
internal static class Tier2Encoder {

  /// <summary>
  /// Assembles LRCP packets for one tile. The writer currently emits one quality layer and the
  /// default precinct partition, so each resolution/component pair is one packet for the dimensions
  /// accepted by the image writer.
  /// </summary>
  public static byte[] AssemblePackets(List<CodeBlockData> codeBlocks, TileInfo tile) {
    ArgumentNullException.ThrowIfNull(codeBlocks);
    ArgumentNullException.ThrowIfNull(tile);
    if (tile.Layers != 1)
      throw new NotSupportedException("The JPEG 2000 encoder currently assigns all coding passes to one quality layer.");

    var subbands = SubbandInfo.ComputeSubbands(tile.Width, tile.Height, tile.DecompLevels);
    var codeBlockMap = new Dictionary<(int Subband, int X, int Y), CodeBlockData>();
    foreach (var block in codeBlocks)
      codeBlockMap[(block.SubbandIndex, block.CodeBlockX, block.CodeBlockY)] = block;

    using var output = new MemoryStream();

    // LRCP means layer, resolution, component, position. With the default precinct partition and the
    // zero-decomposition authoring path, there is one packet per component. The resolution loop is
    // still explicit because the reader uses this same packet grammar for existing codestreams.
    for (var layer = 0; layer < tile.Layers; ++layer)
      for (var resolution = 0; resolution <= tile.DecompLevels; ++resolution)
        for (var component = 0; component < tile.ComponentCount; ++component)
          _WritePacket(output, codeBlockMap, subbands, tile, component, resolution, layer);

    return output.ToArray();
  }

  private static void _WritePacket(
    Stream output,
    IReadOnlyDictionary<(int Subband, int X, int Y), CodeBlockData> codeBlockMap,
    SubbandInfo[] subbands,
    TileInfo tile,
    int component,
    int resolution,
    int layer
  ) {
    var packetSubbands = _SubbandsForResolution(subbands, tile.DecompLevels, resolution);
    var componentOffset = component * subbands.Length;
    var hasData = false;

    foreach (var subband in packetSubbands) {
      subband.GetCodeBlockGrid(tile.CodeBlockWidth, tile.CodeBlockHeight, out var countX, out var countY);
      for (var y = 0; y < countY && !hasData; ++y)
        for (var x = 0; x < countX; ++x)
          if (codeBlockMap.TryGetValue((subband.Index + componentOffset, x, y), out var block)
              && block.CompressedData.Length > 0 && block.NumCodingPasses > 0) {
            hasData = true;
            break;
          }
    }

    var header = new BitWriter();
    header.WriteBit(hasData ? 1 : 0); // B.10.3 zero-length packet bit

    if (!hasData) {
      output.Write(header.Flush());
      return;
    }

    var bodies = new List<byte[]>();

    foreach (var subband in packetSubbands) {
      subband.GetCodeBlockGrid(tile.CodeBlockWidth, tile.CodeBlockHeight, out var countX, out var countY);
      if (countX == 0 || countY == 0)
        continue;

      var inclusion = new TagTree(countX, countY);
      var zeroBitPlanes = new TagTree(countX, countY);

      for (var y = 0; y < countY; ++y)
        for (var x = 0; x < countX; ++x)
          if (codeBlockMap.TryGetValue((subband.Index + componentOffset, x, y), out var block)
              && block.CompressedData.Length > 0 && block.NumCodingPasses > 0) {
            inclusion.SetValue(x, y, layer);
            zeroBitPlanes.SetValue(x, y, block.ZeroBitPlanes);
          }

      for (var y = 0; y < countY; ++y)
        for (var x = 0; x < countX; ++x) {
          var key = (subband.Index + componentOffset, x, y);
          if (!inclusion.Encode(x, y, layer + 1, header))
            continue;

          var block = codeBlockMap[key];
          if (!zeroBitPlanes.Encode(x, y, block.ZeroBitPlanes + 1, header))
            throw new InvalidDataException("JPEG 2000 zero-bit-plane tag tree failed to publish an included block.");

          _WriteNumCodingPasses(header, block.NumCodingPasses);
          _WriteDataLength(header, block.CompressedData.Length, block.NumCodingPasses);

          if (block.CompressedData[^1] == 0xFF)
            throw new InvalidDataException("A JPEG 2000 code-block contribution may not end in 0xFF (T.800 B.10.7).");

          bodies.Add(block.CompressedData);
        }
    }

    // B.9: unless PPM/PPT relocates it, each packet header sits immediately before that packet's
    // body. The previous implementation emitted every header first and every body afterwards.
    output.Write(header.Flush());
    foreach (var body in bodies)
      output.Write(body);
  }

  private static SubbandInfo[] _SubbandsForResolution(SubbandInfo[] subbands, int levels, int resolution) {
    var result = new List<SubbandInfo>(resolution == 0 ? 1 : 3);
    foreach (var subband in subbands) {
      if (resolution == 0) {
        if (subband.Type == 0)
          result.Add(subband);
        continue;
      }

      var level = levels - resolution + 1;
      if (subband.Type != 0 && subband.ResolutionLevel == level)
        result.Add(subband);
    }
    return result.ToArray();
  }

  /// <summary>Writes Table B.4 exactly.</summary>
  private static void _WriteNumCodingPasses(BitWriter writer, int passes) {
    switch (passes) {
      case 1:
        writer.WriteBit(0);
        return;
      case 2:
        writer.WriteBits(0b10, 2);
        return;
      case 3:
        writer.WriteBits(0b1100, 4);
        return;
      case 4:
        writer.WriteBits(0b1101, 4);
        return;
      case 5:
        writer.WriteBits(0b1110, 4);
        return;
      case >= 6 and <= 36:
        writer.WriteBits(0b1111, 4);
        writer.WriteBits(passes - 6, 5);
        return;
      case >= 37 and <= 164:
        writer.WriteBits(0b1111, 4);
        writer.WriteBits(0b11111, 5);
        writer.WriteBits(passes - 37, 7);
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(passes), "JPEG 2000 packet headers can signal 1 to 164 coding passes.");
    }
  }

  /// <summary>Writes B.10.7.1's Lblock increment followed by the contribution length.</summary>
  private static void _WriteDataLength(BitWriter writer, int dataLength, int numPasses) {
    if (dataLength <= 0)
      throw new ArgumentOutOfRangeException(nameof(dataLength));

    var passBits = Tier2Decoder.FloorLog2(numPasses);
    var needed = FloorLog2(dataLength) + 1;
    var lblock = 3;
    while (lblock + passBits < needed)
      ++lblock;

    for (var i = 3; i < lblock; ++i)
      writer.WriteBit(1);
    writer.WriteBit(0);
    writer.WriteBits(dataLength, lblock + passBits);
  }

  private static int FloorLog2(int value) {
    if (value <= 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    var result = 0;
    while (value > 1) {
      ++result;
      value >>= 1;
    }
    return result;
  }
}
