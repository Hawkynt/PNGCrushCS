using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CcittFormat_HasExpectedValues() {
    Assert.That((int)CcittFormat.Group3_1D, Is.EqualTo(0));
    Assert.That((int)CcittFormat.Group3_2D, Is.EqualTo(1));
    Assert.That((int)CcittFormat.Group4, Is.EqualTo(2));

    var values = Enum.GetValues<CcittFormat>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void CcittFile_DefaultPixelData_IsEmpty() {
    var file = new CcittFile { Width = 8, Height = 1, Format = CcittFormat.Group3_1D };

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CcittFile_InitProperties_StoreCorrectly() {
    var pixelData = new byte[] { 0xFF };
    var file = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group4,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Format, Is.EqualTo(CcittFormat.Group4));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }
}
