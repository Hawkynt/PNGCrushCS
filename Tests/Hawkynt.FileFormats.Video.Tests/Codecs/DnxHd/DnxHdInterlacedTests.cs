using FileFormat.Core;

namespace FileFormat.Codecs.DnxHd.Tests;

[TestFixture]
public sealed class DnxHdInterlacedTests {

  [Test]
  [Category("Unit")]
  public void FieldEncodedCodingUnitsAreWovenByTheirFfcParity() {
    var first = _FieldUnit(8, 2);
    var second = _FieldUnit(15, 3);
    var packet = new byte[606_208];
    first.CopyTo(packet, 0);
    second.CopyTo(packet, 303_104);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 32));
    var planes = decoder.DecodePlanes(packet, out var header);

    Assert.Multiple(() => {
      Assert.That(header.FieldFrameCount, Is.EqualTo(2));
      Assert.That(header.DisplayHeight, Is.EqualTo(32));
      Assert.That(planes.Height, Is.EqualTo(32));
    });

    for (var y = 0; y < 32; ++y) {
      var expected = (ushort)((y & 1) == 0 ? 129 : 130);
      for (var x = 0; x < 16; ++x)
        Assert.That(planes.Luma[y * planes.Width + x], Is.EqualTo(expected),
          $"field weave differs at ({x},{y})");
    }
  }

  [Test]
  [Category("Unit")]
  public void Cid1260FieldMacroblockAlternatesRasterLinesInsideOneFrameMacroblock() {
    var payload = new DnxHdTestStream()
      .Bits(1, 1)
      .Bits(1, 10)
      .Bits(0, 1)
      .DcBlock(8).FlatBlock().FlatBlock().FlatBlock()
      .DcBlock(8).FlatBlock().FlatBlock().FlatBlock();

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1260,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      Interlaced = true,
      FrameEncoded = true,
      AdaptiveMacroblocks = true,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    var planes = decoder.DecodePlanes(unit, out var header);

    Assert.Multiple(() => {
      Assert.That(header.FieldFrameCount, Is.EqualTo(1));
      Assert.That(header.AdaptiveMacroblocks, Is.True);
      Assert.That(header.FrameEncoded, Is.True);
      Assert.That(DnxHdProfile.Find(1260)!.FrameSize, Is.EqualTo(417_792),
        "ST 2019-1:2016 Amd 1:2023 makes CID 1260 one frame coding unit");
    });

    for (var y = 0; y < 16; ++y) {
      var expected = (ushort)((y & 1) == 0 ? 129 : 130);
      for (var x = 0; x < 16; ++x)
        Assert.That(planes.Luma[y * planes.Width + x], Is.EqualTo(expected),
          $"adaptive field macroblock differs at ({x},{y})");
    }
  }

  [Test]
  [Category("Unit")]
  public void Cid1260FrameMacroblockKeepsTopAndBottomEightLineBlocksContiguous() {
    var payload = new DnxHdTestStream()
      .Bits(0, 1)
      .Bits(1, 10)
      .Bits(0, 1)
      .DcBlock(8).FlatBlock().FlatBlock().FlatBlock()
      .DcBlock(8).FlatBlock().FlatBlock().FlatBlock();

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1260,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      Interlaced = true,
      FrameEncoded = true,
      AdaptiveMacroblocks = true,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    var planes = decoder.DecodePlanes(unit, out _);

    for (var y = 0; y < 16; ++y) {
      var expected = (ushort)(y < 8 ? 129 : 130);
      for (var x = 0; x < 16; ++x)
        Assert.That(planes.Luma[y * planes.Width + x], Is.EqualTo(expected),
          $"adaptive frame macroblock differs at ({x},{y})");
    }
  }

  private static byte[] _FieldUnit(int dcRho, int fieldFrameCount) {
    var payload = new DnxHdTestStream()
      .Macroblock(1)
      .DcBlock(dcRho).FlatBlock().FlatBlock().FlatBlock()
      .FlatBlock().FlatBlock().FlatBlock().FlatBlock();

    var small = DnxHdTestStream.Unit(new() {
      CompressionId = 1242,
      HeaderVersion = 1,
      Width = 16,
      Height = 16,
      Interlaced = true,
      FrameEncoded = false,
    }, payload);
    small[5] = (byte)((small[5] & ~3) | fieldFrameCount);

    var result = new byte[303_104];
    small.CopyTo(result, 0);
    result[^4] = 0x60;
    result[^3] = 0x0D;
    result[^2] = 0xC0;
    result[^1] = 0xDE;
    return result;
  }
}
