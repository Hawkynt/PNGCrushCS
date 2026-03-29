using System;
using FileFormat.Pict;

namespace FileFormat.Pict.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PictOpcode_HasExpectedValues() {
    Assert.That((ushort)PictOpcode.Version, Is.EqualTo(0x0011));
    Assert.That((ushort)PictOpcode.HeaderOp, Is.EqualTo(0x0C00));
    Assert.That((ushort)PictOpcode.PackBitsRect, Is.EqualTo(0x0098));
    Assert.That((ushort)PictOpcode.DirectBitsRect, Is.EqualTo(0x009A));
    Assert.That((ushort)PictOpcode.EndOfPicture, Is.EqualTo(0x00FF));

    var values = Enum.GetValues<PictOpcode>();
    Assert.That(values, Has.Length.EqualTo(5));
  }
}
