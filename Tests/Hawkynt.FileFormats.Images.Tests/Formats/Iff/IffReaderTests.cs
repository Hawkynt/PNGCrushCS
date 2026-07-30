using System;
using System.IO;
using FileFormat.Iff;

namespace FileFormat.Iff.Tests;

[TestFixture]
public sealed class IffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => IffReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => IffReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() =>
    Assert.Throws<FileNotFoundException>(() => IffReader.FromFile(new FileInfo("nonexistent.iff")));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => IffReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() =>
    Assert.Throws<InvalidDataException>(() => IffReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[12];
    data[0] = (byte)'X';
    data[1] = (byte)'I';
    data[2] = (byte)'F';
    data[3] = (byte)'F';
    Assert.Throws<InvalidDataException>(() => IffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIff_ParsesFormType() {
    var bytes = IffWriter.ToBytes(new IffFile { FormType = "ILBM", Chunks = [] });
    var file = IffReader.FromBytes(bytes);
    Assert.That(file.FormType.ToString(), Is.EqualTo("ILBM"));
  }
}
