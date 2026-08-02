using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// How the MSX widens its palette channels, shared rather than worked out afresh per format.
/// </summary>
/// <remarks>
/// Three formats each divided by seven and truncated, which gives 145 for a value of four where
/// repeating the bits gives 146 — one step in 255, and the whole of the difference from every other
/// decoder.
/// <para/>
/// Screen 8's blue is the harder one. It has two bits where red and green have three, and the
/// machine lifts it onto the three-bit scale through 0, 2, 4, 7 rather than doubling. The last step
/// is larger so the brightest blue still reaches 255: doubling throughout stops at 219 and leaves
/// white looking short of white, which is exactly what two real samples showed.
/// <para/>
/// Both now match RECOIL on every pixel.
/// </remarks>
[TestFixture]
public sealed class MsxChannelScaleTests {

  [Test]
  [Category("Unit")]
  public void ThreeBitsAreWidenedByRepeatingThem() {
    Assert.Multiple(() => {
      Assert.That(MsxGraphics.Expand3(0), Is.Zero);
      Assert.That(MsxGraphics.Expand3(4), Is.EqualTo(146), "dividing by seven would give 145");
      Assert.That(MsxGraphics.Expand3(7), Is.EqualTo(255), "the brightest must reach white");
    });
  }

  [Test]
  [Category("Unit")]
  public void TwoBitBlueClimbsTheThreeBitScaleAndReachesWhite() {
    Assert.Multiple(() => {
      Assert.That(MsxGraphics.Expand2(0), Is.Zero);
      Assert.That(MsxGraphics.Expand2(1), Is.EqualTo(73));
      Assert.That(MsxGraphics.Expand2(2), Is.EqualTo(146));
      Assert.That(MsxGraphics.Expand2(3), Is.EqualTo(255), "not 219, which doubling throughout gives");
    });
  }

  [Test]
  [Category("Unit")]
  public void BlueIsNotSimplyTheTwoBitsRepeated() {
    // Repeating gives 85 and 170, which puts every blue in the picture too high.
    Assert.Multiple(() => {
      Assert.That(MsxGraphics.Expand2(1), Is.Not.EqualTo(0x55));
      Assert.That(MsxGraphics.Expand2(2), Is.Not.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryChannelRisesWithItsValue() {
    Assert.Multiple(() => {
      for (var i = 1; i < 8; ++i)
        Assert.That(MsxGraphics.Expand3(i), Is.GreaterThan(MsxGraphics.Expand3(i - 1)), $"three-bit {i}");
      for (var i = 1; i < 4; ++i)
        Assert.That(MsxGraphics.Expand2(i), Is.GreaterThan(MsxGraphics.Expand2(i - 1)), $"two-bit {i}");
    });
  }
}
