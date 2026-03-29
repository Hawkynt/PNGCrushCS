using System;
using System.IO;
using FileFormat.CDUPaint;

namespace FileFormat.CDUPaint.Tests;

[TestFixture]
public sealed class CDUPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CDUPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CDUPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cdu"));
    Assert.Throws<FileNotFoundException>(() => CDUPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CDUPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => CDUPaintReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => CDUPaintReader.FromBytes(new byte[10004]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidFile(0x6000, 0x03);
    var result = CDUPaintReader.FromBytes(data);

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
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[CDUPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;
    var result = CDUPaintReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = _BuildValidFile(0x6000, 0x00);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = CDUPaintReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidFile(0x6000, 0x05);
    using var ms = new MemoryStream(data);
    var result = CDUPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var videoMatrix = new byte[1000];
    for (var i = 0; i < videoMatrix.Length; ++i)
      videoMatrix[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new CDUPaintFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 11,
    };

    var bytes = CDUPaintWriter.ToBytes(original);
    var restored = CDUPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor) {
    var data = new byte[CDUPaintFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;

    return data;
  }
}
