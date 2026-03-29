using FileFormat.Mpo;

namespace FileFormat.Mpo.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MpoFile_DefaultImages_IsEmpty() {
    var file = new MpoFile();

    Assert.That(file.Images, Is.Not.Null);
    Assert.That(file.Images.Count, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MpoFile_WithImages_StoresCorrectCount() {
    var file = new MpoFile {
      Images = [new byte[] { 0xFF, 0xD8 }, new byte[] { 0xFF, 0xD8 }]
    };

    Assert.That(file.Images.Count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => MpoFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EmptyImages_ThrowsArgumentException() {
    var file = new MpoFile { Images = [] };

    Assert.Throws<System.ArgumentException>(() => MpoFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullImage_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => MpoFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ValidImage_CreatesSingleImageMpo() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[2 * 2 * 3]
    };

    var result = MpoFile.FromRawImage(raw);

    Assert.That(result.Images.Count, Is.EqualTo(1));
    Assert.That(result.Images[0][0], Is.EqualTo(0xFF));
    Assert.That(result.Images[0][1], Is.EqualTo(0xD8));
  }
}
