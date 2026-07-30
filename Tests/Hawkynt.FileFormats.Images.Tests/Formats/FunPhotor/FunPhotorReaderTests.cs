using System;
using System.IO;
using FileFormat.FunPhotor;

namespace FileFormat.FunPhotor.Tests;

[TestFixture]
public sealed class FunPhotorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr"));
    Assert.Throws<FileNotFoundException>(() => FunPhotorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FunPhotorReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FunPhotorReader.FromBytes(new byte[10051]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[FunPhotorFile.ExpectedFileSize];
    data[0] = 0x00; // load address low
    data[1] = 0x60; // load address high = 0x6000

    var result = FunPhotorReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
      Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
      Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
      Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var data = new byte[FunPhotorFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;
    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var file = FunPhotorReader.FromBytes(data);
    var written = FunPhotorWriter.ToBytes(file);
    var reread = FunPhotorReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.LoadAddress, Is.EqualTo(file.LoadAddress));
      Assert.That(reread.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(reread.ScreenData, Is.EqualTo(file.ScreenData));
      Assert.That(reread.ColorData, Is.EqualTo(file.ColorData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[FunPhotorFile.ExpectedFileSize];
    using var ms = new MemoryStream(data);
    var result = FunPhotorReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
  }
}
