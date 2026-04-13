using FileFormat.Gbr;

namespace FileFormat.Gbr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new GbrFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultName_IsNull() {
    var file = new GbrFile();
    Assert.That(file.Name, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultWidth_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultHeight_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultBytesPerPixel_IsZero() {
    var file = new GbrFile();
    Assert.That(file.BytesPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultSpacing_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Spacing, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new GbrFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Spacing = 42,
      Name = "Test Brush",
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.Spacing, Is.EqualTo(42));
    Assert.That(file.Name, Is.EqualTo("Test Brush"));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }
}
