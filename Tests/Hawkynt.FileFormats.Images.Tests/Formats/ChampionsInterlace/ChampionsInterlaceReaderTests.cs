using System;
using System.IO;
using FileFormat.ChampionsInterlace;

namespace FileFormat.ChampionsInterlace.Tests;

[TestFixture]
public sealed class ChampionsInterlaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ChampionsInterlaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ChampionsInterlaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cin"));
    Assert.Throws<FileNotFoundException>(() => ChampionsInterlaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ChampionsInterlaceReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ChampionsInterlaceReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidFile(0x6000, 0x00);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmap1() {
    var data = _BuildValidFile(0x4000, 0x03);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.Bitmap1.Length, Is.EqualTo(8000));
    Assert.That(result.Bitmap1[0], Is.EqualTo(0xAB));
    Assert.That(result.Bitmap1[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreen1() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.Screen1.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesColorData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmap2() {
    var data = _BuildValidFile(0x4000, 0x03);
    // Bitmap2 starts at offset 2 + 8000 + 1000 + 1000 = 10002
    data[10002] = 0xEE;
    data[18001] = 0xFF;

    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.Bitmap2.Length, Is.EqualTo(8000));
    Assert.That(result.Bitmap2[0], Is.EqualTo(0xEE));
    Assert.That(result.Bitmap2[7999], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreen2() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.Screen2.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBackgroundColor() {
    var data = _BuildValidFile(0x4000, 0x07);
    var result = ChampionsInterlaceReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000, 0x05);
    using var ms = new MemoryStream(data);
    var result = ChampionsInterlaceReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.Bitmap1.Length, Is.EqualTo(8000));
    Assert.That(result.Screen1.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    Assert.That(result.Bitmap2.Length, Is.EqualTo(8000));
    Assert.That(result.Screen2.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor) {
    var data = new byte[ChampionsInterlaceFile.FileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    // Fill bitmap1
    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    // Fill screen1
    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    // Fill color data
    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    // Fill bitmap2
    for (var i = 0; i < 8000; ++i)
      data[10002 + i] = (byte)((i + 1) % 256);

    // Fill screen2
    for (var i = 0; i < 1000; ++i)
      data[18002 + i] = (byte)((i + 5) % 16);

    // Background color
    data[19002] = backgroundColor;

    return data;
  }
}
