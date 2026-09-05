using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Assembles a tile's packets from its encoded code-blocks (ITU-T T.800 B.9 to B.12).</summary>
/// <remarks>
/// One quality layer, so every code-block's passes go into the single packet its precinct owns. The
/// header still goes through the tag trees, because inclusion and the zero bit-plane count are what
/// tag trees are for and a decoder has no other way to read them.
/// </remarks>
internal static class Tier2Encoder {

  public static byte[] AssemblePackets(Jp2Image image, Jp2Tile tile) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(tile);
    if (tile.Layers != 1)
      throw new NotSupportedException("The JPEG 2000 encoder puts every coding pass into a single quality layer.");

    using var output = new MemoryStream();
    foreach (var packet in Jp2PacketSequence.Enumerate(image, tile))
      _WritePacket(output, tile, packet);

    return output.ToArray();
  }

  private static void _WritePacket(Stream output, Jp2Tile tile, Jp2PacketIndex packet) {
    var resolution = tile.Components[packet.Component].Resolutions[packet.Resolution];

    var bands = new List<Jp2Band>(resolution.Bands.Length);
    var hasData = false;
    foreach (var band in resolution.Bands) {
      if (band.Width <= 0 || band.Height <= 0)
        continue;

      bands.Add(band);
      foreach (var block in band.Precincts[packet.Precinct].CodeBlocks)
        if (block.TotalPasses > 0)
          hasData = true;
    }

    var header = new BitWriter();
    header.WriteBit(hasData ? 1 : 0);

    if (!hasData) {
      output.Write(header.Flush());
      return;
    }

    var bodies = new List<byte[]>();

    foreach (var band in bands) {
      var precinct = band.Precincts[packet.Precinct];
      if (precinct.CodeBlocks.Length == 0)
        continue;

      var inclusion = precinct.Inclusion ??= new(precinct.CodeBlocksWide, precinct.CodeBlocksHigh);
      var zeroBitPlanes = precinct.ZeroBitPlanes ??= new(precinct.CodeBlocksWide, precinct.CodeBlocksHigh);

      for (var index = 0; index < precinct.CodeBlocks.Length; ++index) {
        var block = precinct.CodeBlocks[index];
        var x = index % precinct.CodeBlocksWide;
        var y = index / precinct.CodeBlocksWide;

        if (block.TotalPasses > 0)
          inclusion.SetValue(x, y, packet.Layer);

        // Every leaf needs a value before the first bit goes out, or a parent's minimum is taken
        // from whichever sibling happened to be visited first.
        zeroBitPlanes.SetValue(x, y, block.ZeroBitPlanes);
      }

      for (var index = 0; index < precinct.CodeBlocks.Length; ++index) {
        var block = precinct.CodeBlocks[index];
        var x = index % precinct.CodeBlocksWide;
        var y = index / precinct.CodeBlocksWide;

        if (!inclusion.Encode(x, y, packet.Layer + 1, header))
          continue;

        if (!zeroBitPlanes.Encode(x, y, block.ZeroBitPlanes + 1, header))
          throw new InvalidDataException("JPEG 2000 zero bit-plane tag tree failed to publish an included code-block.");

        _WritePassCount(header, block.TotalPasses);
        _WriteLength(header, block, block.Encoded.Length);
        bodies.Add(block.Encoded);
      }
    }

    output.Write(header.Flush());
    foreach (var body in bodies)
      output.Write(body);
  }

  /// <summary>Table B.4.</summary>
  private static void _WritePassCount(BitWriter writer, int passes) {
    switch (passes) {
      case 1:
        writer.WriteBit(0);
        return;
      case 2:
        writer.WriteBits(0b10, 2);
        return;
      case >= 3 and <= 5:
        writer.WriteBits(0b11, 2);
        writer.WriteBits(passes - 3, 2);
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
        throw new ArgumentOutOfRangeException(nameof(passes), "A packet header can signal 1 to 164 coding passes.");
    }
  }

  /// <summary>B.10.7.1: as many ones as Lblock has to grow, a zero, then the length itself.</summary>
  private static void _WriteLength(BitWriter writer, Jp2CodeBlock block, int length) {
    if (length <= 0)
      throw new ArgumentOutOfRangeException(nameof(length));

    var passBits = Tier2Decoder.FloorLog2(block.TotalPasses);
    var needed = Tier2Decoder.FloorLog2(length) + 1;
    var increment = 0;
    while (block.Lblock + increment + passBits < needed)
      ++increment;

    for (var i = 0; i < increment; ++i)
      writer.WriteBit(1);
    writer.WriteBit(0);

    block.Lblock += increment;
    writer.WriteBits(length, block.Lblock + passBits);
  }
}
