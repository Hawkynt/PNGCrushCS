using System;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000.Tests;

/// <summary>The bit-level shape of a packet header (ITU-T T.800 B.10).</summary>
[TestFixture]
public sealed class Tier2PacketSyntaxTests {

  private static (Jp2Image Image, Jp2Tile Tile) _BuildOneByOneTile() {
    var style = new Jp2CodingStyle {
      DecompositionLevels = 0,
      CodeBlockWidthExp = 6,
      CodeBlockHeightExp = 6,
      Transform = 1,
      QuantizationStyle = 0,
      GuardBits = 2,
      QuantExponents = [8],
      QuantMantissas = [0],
    };
    style.UseDefaultPrecincts();

    var image = new Jp2Image {
      X1 = 1,
      Y1 = 1,
      TileWidth = 1,
      TileHeight = 1,
      Components = [new()],
    };

    return (image, Jp2StructureBuilder.Build(image, 0, [style], 1, 0, false, false, false, allocateCoefficients: true));
  }

  [Test]
  [Category("Unit")]
  public void SingleBlockPacket_PutsItsBodyImmediatelyAfterItsHeader() {
    var (image, tile) = _BuildOneByOneTile();
    var block = tile.Components[0].Resolutions[0].Bands[0].Precincts[0].CodeBlocks[0];
    block.Encoded = [0x12, 0x34];
    block.TotalPasses = 1;
    block.ZeroBitPlanes = 0;

    var bytes = Tier2Encoder.AssemblePackets(image, tile);

    // Non-empty 1, inclusion tag tree 1, zero bit-plane tag tree 1, one pass 0, Lblock unchanged 0,
    // then a two-byte length in three bits, 010. That is 11100010, and the body follows it.
    Assert.That(bytes, Is.EqualTo(new byte[] { 0xE2, 0x12, 0x34 }));

    var (_, readTile) = _BuildOneByOneTile();
    Tier2Decoder.ReadPackets(bytes, 0, bytes.Length, image, readTile);

    var decoded = readTile.Components[0].Resolutions[0].Bands[0].Precincts[0].CodeBlocks[0];
    Assert.Multiple(() => {
      Assert.That(decoded.Included, Is.True);
      Assert.That(decoded.TotalPasses, Is.EqualTo(1));
      Assert.That(decoded.ZeroBitPlanes, Is.Zero);
      Assert.That(decoded.Data.ToArray(), Is.EqualTo(new byte[] { 0x12, 0x34 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void EmptyPacket_IsOneZeroBitAndNothingElse() {
    var (image, tile) = _BuildOneByOneTile();
    var bytes = Tier2Encoder.AssemblePackets(image, tile);

    Assert.That(bytes, Is.EqualTo(new byte[] { 0x00 }));

    var (_, readTile) = _BuildOneByOneTile();
    Tier2Decoder.ReadPackets(bytes, 0, bytes.Length, image, readTile);
    Assert.That(readTile.Components[0].Resolutions[0].Bands[0].Precincts[0].CodeBlocks[0].Included, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void PacketHeaderEndingInFfCarriesTheMandatoryStuffedZeroByte() {
    var writer = new BitWriter();
    for (var i = 0; i < 8; ++i)
      writer.WriteBit(1);

    var bytes = writer.Flush();
    Assert.That(bytes, Is.EqualTo(new byte[] { 0xFF, 0x00 }));

    var reader = new BitReader(bytes, 0, bytes.Length);
    for (var i = 0; i < 8; ++i)
      Assert.That(reader.ReadBit(), Is.EqualTo(1));

    reader.AlignToByte();
    Assert.That(reader.Position, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TheBitAfterAnFfIsStuffedAndCarriesNoSyntax() {
    // 0xFF then 0x40: the second byte's top bit is the stuffed zero, so its seven syntax bits are
    // 1000000 and a reader that took eight would be one bit out from here to the end of the header.
    var reader = new BitReader([0xFF, 0x40], 0, 2);
    for (var i = 0; i < 8; ++i)
      Assert.That(reader.ReadBit(), Is.EqualTo(1), $"bit {i}");

    Assert.Multiple(() => {
      Assert.That(reader.ReadBit(), Is.EqualTo(1), "first syntax bit of the stuffed byte");
      for (var i = 1; i < 7; ++i)
        Assert.That(reader.ReadBit(), Is.Zero, $"stuffed byte bit {i}");
    });
  }
}
