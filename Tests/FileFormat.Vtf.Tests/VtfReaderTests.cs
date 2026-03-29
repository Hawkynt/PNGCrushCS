using System;
using System.IO;
using FileFormat.Vtf;

namespace FileFormat.Vtf.Tests;

[TestFixture]
public sealed class VtfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VtfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VtfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vtf"));
    Assert.Throws<FileNotFoundException>(() => VtfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VtfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => VtfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[VtfHeader.StructSize];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = 0;
    Assert.Throws<InvalidDataException>(() => VtfReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba8888_ParsesCorrectly() {
    var vtf = VtfTestHelper.BuildMinimalVtf(4, 4, VtfFormat.Rgba8888, 1);
    var result = VtfReader.FromBytes(vtf);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Format, Is.EqualTo(VtfFormat.Rgba8888));
    Assert.That(result.MipmapCount, Is.EqualTo(1));
    Assert.That(result.Surfaces, Has.Count.EqualTo(1));
  }
}
