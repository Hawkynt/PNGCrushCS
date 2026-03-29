using FileFormat.SpeccyExtended;
using FileFormat.Core;

namespace FileFormat.SpeccyExtended.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_DefaultBitmapData_IsEmpty() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_DefaultAttributeData_IsEmpty() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_DefaultExtendedAttributeData_IsEmpty() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.ExtendedAttributeData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_DefaultVersion_Is1() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.Version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_Width_IsAlways256() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void SpeccyExtendedFile_Height_IsAlways192() {
    var file = new SpeccyExtendedFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => SpeccyExtendedFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => SpeccyExtendedFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 192 * 3],
    };
    Assert.Throws<System.NotSupportedException>(() => SpeccyExtendedFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var file = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var raw = SpeccyExtendedFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 192 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesExpectedLayout() {
    Assert.That(SpeccyExtendedReader.FileSize, Is.EqualTo(4 + 6144 + 768 + 768));
  }

  [Test]
  [Category("Unit")]
  public void HeaderSize_Is4() {
    Assert.That(SpeccyExtendedReader.HeaderSize, Is.EqualTo(4));
  }
}
