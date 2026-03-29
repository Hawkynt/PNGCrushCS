using System;
using System.Linq;
using FileFormat.Apng;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class ApngFctlTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesFields() {
    var original = new ApngFctl(
      SequenceNumber: 5,
      Width: 320,
      Height: 240,
      XOffset: 10,
      YOffset: 20,
      DelayNum: 100,
      DelayDen: 1000,
      DisposeOp: (byte)ApngDisposeOp.Background,
      BlendOp: (byte)ApngBlendOp.Over
    );

    var buffer = new byte[ApngFctl.StructSize];
    original.WriteTo(buffer);
    var restored = ApngFctl.ReadFrom(buffer);

    Assert.That(restored.SequenceNumber, Is.EqualTo(5));
    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(240));
    Assert.That(restored.XOffset, Is.EqualTo(10));
    Assert.That(restored.YOffset, Is.EqualTo(20));
    Assert.That(restored.DelayNum, Is.EqualTo(100));
    Assert.That(restored.DelayDen, Is.EqualTo(1000));
    Assert.That(restored.DisposeOp, Is.EqualTo((byte)ApngDisposeOp.Background));
    Assert.That(restored.BlendOp, Is.EqualTo((byte)ApngBlendOp.Over));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_Covers26Bytes() {
    var map = ApngFctl.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);

    Assert.That(totalSize, Is.EqualTo(ApngFctl.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is26() {
    Assert.That(ApngFctl.StructSize, Is.EqualTo(26));
  }
}
