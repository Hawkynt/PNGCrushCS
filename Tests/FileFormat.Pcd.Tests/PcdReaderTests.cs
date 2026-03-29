using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

[TestFixture]
public sealed class PcdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pcd"));
    Assert.Throws<FileNotFoundException>(() => PcdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[2059];
    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[PcdFile.HeaderSize + 3];
    data[PcdFile.PreambleSize] = (byte)'X';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PcdFile.PreambleSize + 8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PcdFile.PreambleSize + 10), 1);
    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = _CreateValidData(0, 1, 0);
    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesCorrectly() {
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var data = _CreateValidData(2, 1, pixels.Length);
    Array.Copy(pixels, 0, data, PcdFile.HeaderSize, pixels.Length);

    var result = PcdReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var pixels = new byte[] { 42, 84, 126 };
    var data = _CreateValidData(1, 1, pixels.Length);
    Array.Copy(pixels, 0, data, PcdFile.HeaderSize, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = PcdReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixels = new byte[4 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    var data = _CreateValidData(4, 2, pixels.Length);
    Array.Copy(pixels, 0, data, PcdFile.HeaderSize, pixels.Length);

    var result = PcdReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  private static byte[] _CreateValidData(ushort width, ushort height, int pixelBytes) {
    var data = new byte[PcdFile.HeaderSize + pixelBytes];
    Array.Copy(PcdFile.Magic, 0, data, PcdFile.PreambleSize, PcdFile.Magic.Length);
    var dimOffset = PcdFile.PreambleSize + PcdFile.Magic.Length;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(dimOffset), width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(dimOffset + 2), height);
    return data;
  }
}
