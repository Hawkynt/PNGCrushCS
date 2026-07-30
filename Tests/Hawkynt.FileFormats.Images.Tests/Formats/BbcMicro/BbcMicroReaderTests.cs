using System;
using System.IO;
using FileFormat.BbcMicro;

namespace FileFormat.BbcMicro.Tests;

[TestFixture]
public sealed class BbcMicroReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BbcMicroReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BbcMicroReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bbc"));
    Assert.Throws<FileNotFoundException>(() => BbcMicroReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BbcMicroReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => BbcMicroReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_Mode1_ThrowsInvalidDataException() {
    var wrongSize = new byte[20481];
    Assert.Throws<InvalidDataException>(() => BbcMicroReader.FromBytes(wrongSize, BbcMicroMode.Mode1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode1_ParsesCorrectly() {
    var data = new byte[BbcMicroFile.ScreenSizeModes012];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var result = BbcMicroReader.FromBytes(data, BbcMicroMode.Mode1);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(256));
    Assert.That(result.Mode, Is.EqualTo(BbcMicroMode.Mode1));
    Assert.That(result.PixelData.Length, Is.EqualTo(256 * 80)); // 256 scanlines * 80 bytes
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode0_ParsesDimensions() {
    var data = new byte[BbcMicroFile.ScreenSizeModes012];

    var result = BbcMicroReader.FromBytes(data, BbcMicroMode.Mode0);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(256));
    Assert.That(result.Mode, Is.EqualTo(BbcMicroMode.Mode0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode4_ParsesDimensions() {
    var data = new byte[BbcMicroFile.ScreenSizeModes45];

    var result = BbcMicroReader.FromBytes(data, BbcMicroMode.Mode4);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(256));
    Assert.That(result.Mode, Is.EqualTo(BbcMicroMode.Mode4));
    Assert.That(result.PixelData.Length, Is.EqualTo(256 * 40)); // 256 scanlines * 40 bytes
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultMode_IsMode1() {
    var data = new byte[BbcMicroFile.ScreenSizeModes012];

    var result = BbcMicroReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(BbcMicroMode.Mode1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Mode4_WrongSizeForMode012_ThrowsInvalidDataException() {
    var data = new byte[BbcMicroFile.ScreenSizeModes012]; // 20480 bytes, but mode 4 expects 10240
    Assert.Throws<InvalidDataException>(() => BbcMicroReader.FromBytes(data, BbcMicroMode.Mode4));
  }
}
