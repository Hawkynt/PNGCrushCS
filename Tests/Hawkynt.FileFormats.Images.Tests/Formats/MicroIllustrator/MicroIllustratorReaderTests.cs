using System;
using System.IO;
using FileFormat.MicroIllustrator;

namespace FileFormat.MicroIllustrator.Tests;

[TestFixture]
public sealed class MicroIllustratorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mil"));
    Assert.Throws<FileNotFoundException>(() => MicroIllustratorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MicroIllustratorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MicroIllustratorReader.FromBytes(new byte[10004]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[MicroIllustratorFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;
    data[10002] = 0x03;

    var result = MicroIllustratorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.VideoMatrix.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[MicroIllustratorFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;
    data[10002] = 0x05;

    using var ms = new MemoryStream(data);
    var result = MicroIllustratorReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }
}
