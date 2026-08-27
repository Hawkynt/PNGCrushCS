using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Tier2PacketSyntaxTests {

  [Test]
  [Category("Unit")]
  public void SingleBlockPacket_PutsBodyImmediatelyAfterItsHeader() {
    var tile = new TileInfo {
      Width = 1,
      Height = 1,
      DecompLevels = 0,
      ComponentCount = 1,
      CodeBlockWidth = 64,
      CodeBlockHeight = 64,
      Layers = 1,
      BitsPerComponent = 8,
    };

    var block = new CodeBlockData {
      SubbandIndex = 0,
      CodeBlockX = 0,
      CodeBlockY = 0,
      NumCodingPasses = 1,
      ZeroBitPlanes = 0,
      CompressedData = [0x12, 0x34],
    };

    var bytes = Tier2Encoder.AssemblePackets([block], tile);

    // non-empty=1, inclusion tag tree=1, zero-bit-plane tag tree=1, one pass=0,
    // unchanged Lblock=0, two-byte length in three bits=010 => 11100010 (E2).
    Assert.That(bytes, Is.EqualTo(new byte[] { 0xE2, 0x12, 0x34 }));

    var decoded = Tier2Decoder.ParsePackets(bytes, 0, bytes.Length, tile);
    Assert.That(decoded, Has.Count.EqualTo(1));
    Assert.Multiple(() => {
      Assert.That(decoded[0].NumCodingPasses, Is.EqualTo(1));
      Assert.That(decoded[0].ZeroBitPlanes, Is.Zero);
      Assert.That(decoded[0].CompressedData, Is.EqualTo(new byte[] { 0x12, 0x34 }));
    });
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
}
