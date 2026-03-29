using System;
using System.IO;
using FileFormat.Hireslace;

namespace FileFormat.Hireslace.Tests;

[TestFixture]
public sealed class HireslaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hle"));
    Assert.Throws<FileNotFoundException>(() => HireslaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => HireslaceReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var data = _CreateValidHleData(0x2000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _CreateValidHleData(0x4000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_LoadAddressLE() {
    var data = _CreateValidHleData(0x1234);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x1234));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_Bitmap1Length() {
    var data = _CreateValidHleData(0x2000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Bitmap1.Length, Is.EqualTo(HireslaceFile.BitmapDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_Screen1Length() {
    var data = _CreateValidHleData(0x2000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Screen1.Length, Is.EqualTo(HireslaceFile.ScreenDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_Bitmap2Length() {
    var data = _CreateValidHleData(0x2000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Bitmap2.Length, Is.EqualTo(HireslaceFile.BitmapDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_Screen2Length() {
    var data = _CreateValidHleData(0x2000);
    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Screen2.Length, Is.EqualTo(HireslaceFile.ScreenDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Bitmap1DataPreserved() {
    var data = _CreateValidHleData(0x2000);
    // First bitmap byte is at offset 2
    data[2] = 0xAA;
    data[3] = 0xBB;

    var result = HireslaceReader.FromBytes(data);

    Assert.That(result.Bitmap1[0], Is.EqualTo(0xAA));
    Assert.That(result.Bitmap1[1], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _CreateValidHleData(0x2000);
    using var stream = new MemoryStream(data);

    var result = HireslaceReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  private static byte[] _CreateValidHleData(ushort loadAddress) {
    var data = new byte[HireslaceFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    return data;
  }
}
