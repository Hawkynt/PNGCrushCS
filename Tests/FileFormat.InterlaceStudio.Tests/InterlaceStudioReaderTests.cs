using System;
using System.IO;
using FileFormat.InterlaceStudio;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class InterlaceStudioReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ist"));
    Assert.Throws<FileNotFoundException>(() => InterlaceStudioReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceStudioReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => InterlaceStudioReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[19003];
    data[0] = 0x00;
    data[1] = 0x40;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesLoadAddress_LittleEndian() {
    var data = new byte[19003];
    data[0] = 0x34;
    data[1] = 0x12;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x1234));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesBitmap1() {
    var data = new byte[19003];
    data[2] = 0xAB;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.Bitmap1[0], Is.EqualTo(0xAB));
    Assert.That(result.Bitmap1.Length, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesScreen1() {
    var data = new byte[19003];
    data[8002] = 0xCD;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.Screen1[0], Is.EqualTo(0xCD));
    Assert.That(result.Screen1.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesColorData() {
    var data = new byte[19003];
    data[9002] = 0xEF;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.ColorData[0], Is.EqualTo(0xEF));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesBitmap2() {
    var data = new byte[19003];
    data[10002] = 0x42;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.Bitmap2[0], Is.EqualTo(0x42));
    Assert.That(result.Bitmap2.Length, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesScreen2() {
    var data = new byte[19003];
    data[18002] = 0x99;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.Screen2[0], Is.EqualTo(0x99));
    Assert.That(result.Screen2.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesBackgroundColor() {
    var data = new byte[19003];
    data[19002] = 0x05;

    var result = InterlaceStudioReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[19003];
    data[0] = 0x00;
    data[1] = 0x40;

    using var ms = new MemoryStream(data);
    var result = InterlaceStudioReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }
}
