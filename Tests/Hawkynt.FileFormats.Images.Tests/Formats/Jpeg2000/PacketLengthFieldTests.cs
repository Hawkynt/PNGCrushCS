using System;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000.Tests;

/// <summary>
/// How wide the length field in a packet header is.
/// </summary>
/// <remarks>
/// The standard makes it Lblock bits plus one for every doubling of the coding-pass count. The
/// second term is easy to leave out, and leaving it out is invisible from inside: a writer and a
/// reader that both omit it agree with each other, and with nothing else. OpenJPEG read a length of
/// 48959 where the code-block held 2301 bytes and refused the file.
/// </remarks>
[TestFixture]
public sealed class PacketLengthFieldTests {

  [Test]
  [Category("Unit")]
  public void OneMorePassDoesNotWidenTheFieldUntilItDoubles() {
    Assert.Multiple(() => {
      Assert.That(Tier2Decoder.FloorLog2(1), Is.Zero);
      Assert.That(Tier2Decoder.FloorLog2(2), Is.EqualTo(1));
      Assert.That(Tier2Decoder.FloorLog2(3), Is.EqualTo(1), "three passes is still one doubling");
      Assert.That(Tier2Decoder.FloorLog2(4), Is.EqualTo(2));
      Assert.That(Tier2Decoder.FloorLog2(7), Is.EqualTo(2));
      Assert.That(Tier2Decoder.FloorLog2(8), Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePassCountNeverWidensItBackwards() {
    var previous = 0;
    for (var passes = 1; passes <= 64; ++passes) {
      var bits = Tier2Decoder.FloorLog2(passes);
      Assert.That(bits, Is.GreaterThanOrEqualTo(previous), $"{passes} passes");
      previous = bits;
    }
  }

  [Test]
  [Category("Unit")]
  public void ASinglePassLeavesTheFieldAtLblockAlone()
    => Assert.That(Tier2Decoder.FloorLog2(1), Is.Zero,
      "one pass adds nothing, which is why a file of single-pass blocks hid this for so long");
}
