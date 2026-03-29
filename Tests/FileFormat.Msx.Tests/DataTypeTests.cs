using System;
using FileFormat.Msx;

namespace FileFormat.Msx.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MsxMode_Screen2_Is2() {
    Assert.That((int)MsxMode.Screen2, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MsxMode_Screen5_Is5() {
    Assert.That((int)MsxMode.Screen5, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void MsxMode_Screen7_Is7() {
    Assert.That((int)MsxMode.Screen7, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void MsxMode_Screen8_Is8() {
    Assert.That((int)MsxMode.Screen8, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void MsxMode_HasExpectedCount() {
    var values = Enum.GetValues<MsxMode>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void MsxFile_DefaultPixelData_IsEmpty() {
    var file = new MsxFile {
      Width = 0,
      Height = 0,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1
    };

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MsxFile_HasBloadHeader_DefaultIsFalse() {
    var file = new MsxFile {
      Width = 0,
      Height = 0,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1
    };

    Assert.That(file.HasBloadHeader, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MsxFile_Palette_DefaultIsNull() {
    var file = new MsxFile {
      Width = 0,
      Height = 0,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1
    };

    Assert.That(file.Palette, Is.Null);
  }
}
