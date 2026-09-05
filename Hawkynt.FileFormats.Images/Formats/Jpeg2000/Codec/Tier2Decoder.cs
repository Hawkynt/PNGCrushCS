using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Reads a tile's packets into its code-blocks (ITU-T T.800 B.9 to B.12).</summary>
internal static class Tier2Decoder {

  private const ushort _SOP = 0xFF91;
  private const ushort _EPH = 0xFF92;

  /// <summary>
  /// Parses every packet of one tile from the concatenated bytes of its tile-parts.
  /// </summary>
  /// <remarks>
  /// A codestream may legitimately stop early — that is what a truncated quality layer is — so
  /// running out of bytes ends the walk instead of failing it. What is already decoded stays.
  /// </remarks>
  public static void ReadPackets(byte[] data, int offset, int length, Jp2Image image, Jp2Tile tile) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(tile);
    if ((uint)offset > (uint)data.Length || length < 0 || offset + length > data.Length)
      throw new ArgumentOutOfRangeException(nameof(length));

    var cursor = offset;
    var end = offset + length;

    foreach (var packet in Jp2PacketSequence.Enumerate(image, tile)) {
      if (cursor >= end)
        return;

      cursor = _ReadPacket(data, cursor, end, tile, packet);
    }
  }

  private static int _ReadPacket(byte[] data, int cursor, int end, Jp2Tile tile, Jp2PacketIndex packet) {
    var component = tile.Components[packet.Component];
    var resolution = component.Resolutions[packet.Resolution];

    if (tile.UseSop && cursor + 6 <= end && BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor)) == _SOP)
      cursor += 6;

    var reader = new BitReader(data, cursor, end - cursor);
    var contributions = new List<(Jp2CodeBlock Block, int Passes, int Length)>();

    if (reader.ReadBit() != 0)
      foreach (var band in resolution.Bands) {
        if (band.Width <= 0 || band.Height <= 0)
          continue;

        var precinct = band.Precincts[packet.Precinct];
        if (precinct.CodeBlocks.Length == 0)
          continue;

        precinct.Inclusion ??= new(precinct.CodeBlocksWide, precinct.CodeBlocksHigh);
        precinct.ZeroBitPlanes ??= new(precinct.CodeBlocksWide, precinct.CodeBlocksHigh);

        for (var index = 0; index < precinct.CodeBlocks.Length; ++index) {
          var block = precinct.CodeBlocks[index];
          var x = index % precinct.CodeBlocksWide;
          var y = index / precinct.CodeBlocksWide;

          bool included;
          if (block.Included)
            included = reader.ReadBit() != 0;
          else {
            included = precinct.Inclusion.Decode(x, y, packet.Layer + 1, reader);
            if (included) {
              var threshold = 1;
              while (!precinct.ZeroBitPlanes.Decode(x, y, threshold, reader)) {
                ++threshold;
                if (threshold > 74)
                  throw new InvalidDataException("JPEG 2000 zero bit-plane tag tree ran past any representable precision.");
              }

              block.ZeroBitPlanes = threshold - 1;
              block.Included = true;
            }
          }

          if (!included)
            continue;

          var passes = _ReadPassCount(reader);
          while (reader.ReadBit() != 0) {
            ++block.Lblock;
            if (block.Lblock > 28)
              throw new InvalidDataException("JPEG 2000 Lblock grew past a representable contribution length.");
          }

          var lengthBits = block.Lblock + FloorLog2(passes);
          if (lengthBits > 31)
            throw new InvalidDataException($"JPEG 2000 contribution length needs {lengthBits} bits, which no byte array can index.");

          contributions.Add((block, passes, reader.ReadBits(lengthBits)));
        }
      }

    reader.AlignToByte();
    var body = reader.Position;

    if (tile.UseEph) {
      if (body + 2 > end || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body)) != _EPH)
        throw new InvalidDataException("JPEG 2000 codestream declares EPH markers but a packet header is not followed by one.");
      body += 2;
    }

    foreach (var contribution in contributions) {
      if (contribution.Length < 0 || body + contribution.Length > end)
        throw new InvalidDataException(
          $"JPEG 2000 code-block contribution claims {contribution.Length} bytes with {end - body} left in the tile.");

      contribution.Block.Data.Write(data, body, contribution.Length);
      contribution.Block.TotalPasses += contribution.Passes;
      body += contribution.Length;
    }

    return body;
  }

  /// <summary>Table B.4.</summary>
  private static int _ReadPassCount(BitReader reader) {
    if (reader.ReadBit() == 0)
      return 1;
    if (reader.ReadBit() == 0)
      return 2;

    var value = reader.ReadBits(2);
    if (value < 3)
      return 3 + value;

    value = reader.ReadBits(5);
    if (value < 31)
      return 6 + value;

    return 37 + reader.ReadBits(7);
  }

  internal static int FloorLog2(int value) {
    if (value <= 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    var bits = 0;
    while (value > 1) {
      value >>= 1;
      ++bits;
    }

    return bits;
  }
}
