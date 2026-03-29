using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class FpxReaderTests {

  private static byte[] _BuildValidFpx(int width, int height, byte[]? pixels = null) {
    var pixelCount = width * height * 3;
    var data = new byte[FpxHeader.StructSize + pixelCount];
    data[0] = (byte)'F';
    data[1] = (byte)'P';
    data[2] = (byte)'X';
    data[3] = 0x00;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
    if (pixels != null)
      Array.Copy(pixels, 0, data, FpxHeader.StructSize, Math.Min(pixels.Length, pixelCount));

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpx"));
    Assert.Throws<FileNotFoundException>(() => FpxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[FpxHeader.StructSize + 3];
    data[0] = (byte)'B';
    data[1] = (byte)'A';
    data[2] = (byte)'D';
    data[3] = 0x00;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = _BuildValidFpx(1, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = _BuildValidFpx(1, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0);
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = _BuildValidFpx(4, 3);

    var result = FpxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPixelData() {
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var data = _BuildValidFpx(2, 1, pixels);

    var result = FpxReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var pixels = new byte[] { 42, 84, 126 };
    var data = _BuildValidFpx(1, 1, pixels);

    using var ms = new MemoryStream(data);
    var result = FpxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }
}
