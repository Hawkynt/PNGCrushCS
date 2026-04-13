using FileFormat.Avs;

namespace FileFormat.Avs.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new AvsFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AvsFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new AvsFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }
}
