using System;
using FileFormat.Bsave;

namespace FileFormat.Bsave.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BsaveMode_Cga320x200x4_Is0() {
    Assert.That((int)BsaveMode.Cga320x200x4, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMode_Ega640x350x16_Is1() {
    Assert.That((int)BsaveMode.Ega640x350x16, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMode_Vga320x200x256_Is2() {
    Assert.That((int)BsaveMode.Vga320x200x256, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMode_Cga640x200x2_Is3() {
    Assert.That((int)BsaveMode.Cga640x200x2, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMode_HasExpectedCount() {
    var values = Enum.GetValues<BsaveMode>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
