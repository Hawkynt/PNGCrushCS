using System;
using System.IO;
using FileFormat.AmstradCpc;

namespace FileFormat.AmstradCpc.Tests;

[TestFixture]
public sealed class AmstradCpcReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmstradCpcReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmstradCpcReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scr"));
    Assert.Throws<FileNotFoundException>(() => AmstradCpcReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmstradCpcReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AmstradCpcReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[20000];
    Assert.Throws<InvalidDataException>(() => AmstradCpcReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode1_ParsesCorrectly() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var result = AmstradCpcReader.FromBytes(data, AmstradCpcMode.Mode1);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AmstradCpcMode.Mode1));
    Assert.That(result.PixelData.Length, Is.EqualTo(16000)); // 200 lines * 80 bytes
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode0_ParsesDimensions() {
    var data = new byte[16384];

    var result = AmstradCpcReader.FromBytes(data, AmstradCpcMode.Mode0);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AmstradCpcMode.Mode0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode2_ParsesDimensions() {
    var data = new byte[16384];

    var result = AmstradCpcReader.FromBytes(data, AmstradCpcMode.Mode2);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AmstradCpcMode.Mode2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultMode_IsMode1() {
    var data = new byte[16384];

    var result = AmstradCpcReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(AmstradCpcMode.Mode1));
  }
}
