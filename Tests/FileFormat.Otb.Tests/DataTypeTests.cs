using FileFormat.Otb;

namespace FileFormat.Otb.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void OtbFile_DefaultPixelData_IsEmptyArray() {
    var file = new OtbFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void OtbFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00 };
    var file = new OtbFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }
}
