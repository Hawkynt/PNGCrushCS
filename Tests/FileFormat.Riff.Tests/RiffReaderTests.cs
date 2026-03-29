using System;
using System.IO;
using FileFormat.Riff;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class RiffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => RiffReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => RiffReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() =>
    Assert.Throws<FileNotFoundException>(() => RiffReader.FromFile(new FileInfo("nonexistent.riff")));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => RiffReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() =>
    Assert.Throws<InvalidDataException>(() => RiffReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[12];
    data[0] = (byte)'X';
    data[1] = (byte)'I';
    data[2] = (byte)'F';
    data[3] = (byte)'F';
    Assert.Throws<InvalidDataException>(() => RiffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRiff_ParsesFormType() {
    var bytes = RiffWriter.ToBytes(new RiffFile { FormType = "WAVE" });
    var file = RiffReader.FromBytes(bytes);
    Assert.That(file.FormType.ToString(), Is.EqualTo("WAVE"));
  }
}
