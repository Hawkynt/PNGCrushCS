using System;
using System.IO;
using FileFormat.Mng;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class MngReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MngReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MngReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mng"));
    Assert.Throws<FileNotFoundException>(() => MngReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MngReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => MngReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[8];
    bad[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => MngReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleFrame_ParsesCorrectly() {
    var mng = MngTestHelper.BuildMinimalMng(100, 80, 1000, MngTestHelper.BuildMinimalPng());
    var result = MngReader.FromBytes(mng);

    Assert.That(result.Width, Is.EqualTo(100));
    Assert.That(result.Height, Is.EqualTo(80));
    Assert.That(result.TicksPerSecond, Is.EqualTo(1000));
    Assert.That(result.Frames, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMultiFrame_ParsesAllFrames() {
    var png1 = MngTestHelper.BuildMinimalPng();
    var png2 = MngTestHelper.BuildMinimalPng();
    var mng = MngTestHelper.BuildMinimalMng(100, 80, 1000, png1, png2);
    var result = MngReader.FromBytes(mng);

    Assert.That(result.Frames, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var mng = MngTestHelper.BuildMinimalMng(64, 64, 500, MngTestHelper.BuildMinimalPng());
    using var ms = new MemoryStream(mng);
    var result = MngReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(64));
    Assert.That(result.Height, Is.EqualTo(64));
    Assert.That(result.Frames, Has.Count.EqualTo(1));
  }
}
