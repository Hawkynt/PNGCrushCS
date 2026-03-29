using System;
using System.IO;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class HdrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HdrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HdrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hdr"));
    Assert.Throws<FileNotFoundException>(() => HdrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HdrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => HdrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    Assert.Throws<InvalidDataException>(() => HdrReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHdr_ParsesCorrectly() {
    var hdr = HdrTestHelper.BuildMinimalHdr(2, 2);
    var result = HdrReader.FromBytes(hdr);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHdr_ExposureParsed() {
    var hdr = HdrTestHelper.BuildHdrWithExposure(2, 2, 2.5f);
    var result = HdrReader.FromBytes(hdr);

    Assert.That(result.Exposure, Is.EqualTo(2.5f).Within(0.01f));
  }
}
