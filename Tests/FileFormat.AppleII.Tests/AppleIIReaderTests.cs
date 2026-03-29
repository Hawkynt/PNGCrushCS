using System;
using System.IO;
using FileFormat.AppleII;

namespace FileFormat.AppleII.Tests;

[TestFixture]
public sealed class AppleIIReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hgr"));
    Assert.Throws<FileNotFoundException>(() => AppleIIReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AppleIIReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[10000];
    Assert.Throws<InvalidDataException>(() => AppleIIReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHgr_ParsesCorrectly() {
    var data = new byte[8192];
    var result = AppleIIReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(280));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(AppleIIMode.Hgr));
    Assert.That(result.PixelData.Length, Is.EqualTo(192 * 40));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidDhgr_ParsesCorrectly() {
    var data = new byte[16384];
    var result = AppleIIReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(560));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(AppleIIMode.Dhgr));
    Assert.That(result.PixelData.Length, Is.EqualTo(192 * 80));
  }
}
