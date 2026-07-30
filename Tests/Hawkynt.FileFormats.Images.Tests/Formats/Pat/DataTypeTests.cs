using FileFormat.Pat;

namespace FileFormat.Pat.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new PatFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PatFile_DefaultName_IsNull() {
    var file = new PatFile();
    Assert.That(file.Name, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PatFile_DefaultWidth_IsZero() {
    var file = new PatFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PatFile_DefaultHeight_IsZero() {
    var file = new PatFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PatFile_DefaultBytesPerPixel_IsZero() {
    var file = new PatFile();
    Assert.That(file.BytesPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PatFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new PatFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Name = "test",
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.Name, Is.EqualTo("test"));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }
}
