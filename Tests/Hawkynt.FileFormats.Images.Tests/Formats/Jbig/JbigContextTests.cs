using System;
using FileFormat.Jbig;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class JbigContextTests {

  [Test]
  [Category("Unit")]
  public void ContextCount_Is1024() {
    Assert.That(JbigContext.ContextCount, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void Template0_Has10Positions() {
    Assert.That(JbigContext.Template0Positions, Has.Length.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void GetContext_AllZeros_ReturnsZero() {
    var cur = new byte[8];
    var prev1 = new byte[8];
    var prev2 = new byte[8];

    var cx = JbigContext.GetContext(cur, prev1, prev2, 4, 8);

    Assert.That(cx, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GetContext_AllOnes_Returns1023() {
    var cur = new byte[8];
    var prev1 = new byte[8];
    var prev2 = new byte[8];
    Array.Fill(cur, (byte)1);
    Array.Fill(prev1, (byte)1);
    Array.Fill(prev2, (byte)1);

    var cx = JbigContext.GetContext(cur, prev1, prev2, 4, 8);

    Assert.That(cx, Is.EqualTo(1023));
  }

  [Test]
  [Category("Unit")]
  public void GetContext_OutOfBoundsPixels_TreatedAsZero() {
    var cur = new byte[4];
    var prev1 = new byte[4];
    var prev2 = new byte[4];
    Array.Fill(cur, (byte)1);
    Array.Fill(prev1, (byte)1);
    Array.Fill(prev2, (byte)1);

    // At x=0, left neighbors are out of bounds (treated as 0)
    var cx = JbigContext.GetContext(cur, prev1, prev2, 0, 4);

    // Positions (-1,0), (-2,0), (-1,-1), (-2,-1), (-1,-2) are out of bounds
    Assert.That(cx, Is.LessThan(1023));
  }

  [Test]
  [Category("Unit")]
  public void GetContext_ResultAlwaysWithin10Bits() {
    var cur = new byte[16];
    var prev1 = new byte[16];
    var prev2 = new byte[16];
    var random = new Random(42);

    for (var trial = 0; trial < 100; ++trial) {
      for (var i = 0; i < 16; ++i) {
        cur[i] = (byte)(random.Next(2));
        prev1[i] = (byte)(random.Next(2));
        prev2[i] = (byte)(random.Next(2));
      }

      for (var x = 0; x < 16; ++x) {
        var cx = JbigContext.GetContext(cur, prev1, prev2, x, 16);
        Assert.That(cx, Is.GreaterThanOrEqualTo(0).And.LessThan(1024),
          $"Context out of range at trial={trial}, x={x}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void GetContext_OnlyPrev1Set_CorrectBits() {
    var cur = new byte[8];
    var prev1 = new byte[8];
    var prev2 = new byte[8];
    Array.Fill(prev1, (byte)1);

    // Template positions at dy=-1: (2,-1) bit6, (1,-1) bit5, (0,-1) bit4, (-1,-1) bit3, (-2,-1) bit2
    var cx = JbigContext.GetContext(cur, prev1, prev2, 4, 8);

    // bits 6,5,4,3,2 should be set = 0b0001111100 = 0x7C = 124
    Assert.That(cx, Is.EqualTo(0b0001111100));
  }
}
