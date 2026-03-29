using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.SoftImage;

namespace FileFormat.SoftImage.Tests;

[TestFixture]
public sealed class SoftImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic"));
    Assert.Throws<FileNotFoundException>(() => SoftImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[50];
    Assert.Throws<InvalidDataException>(() => SoftImageReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[110];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0xDEADBEEF);
    Assert.Throws<InvalidDataException>(() => SoftImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var data = _BuildMinimalRgb(4, 3);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelData() {
    var data = _BuildMinimalRgb(2, 2);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesComment() {
    var data = _BuildMinimalRgbWithComment(2, 1, "Test Comment");

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Comment, Is.EqualTo("Test Comment"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_HasAlphaIsFalse() {
    var data = _BuildMinimalRgb(2, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_HasAlphaIsTrue() {
    var data = _BuildMinimalRgba(2, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb_Parses() {
    var data = _BuildMinimalRgb(2, 2);

    using var ms = new MemoryStream(data);
    var result = SoftImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesVersion() {
    var data = _BuildMinimalRgb(1, 1);

    var result = SoftImageReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo(3.71f).Within(0.01f));
  }

  private static byte[] _BuildMinimalRgb(int width, int height) {
    using var ms = new MemoryStream();
    var header = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BitConverter.SingleToInt32Bits(3.71f));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(88), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(90), (ushort)height);
    ms.Write(header, 0, 96);

    ms.WriteByte(0);
    ms.WriteByte(8);
    ms.WriteByte(2);
    ms.WriteByte(0x40 | 0x20 | 0x10);

    var pixelCount = width * height;
    for (var i = 0; i < pixelCount; ++i) {
      ms.WriteByte(128);
      ms.WriteByte((byte)(i % 256));
      ms.WriteByte((byte)(i % 256));
      ms.WriteByte((byte)(i % 256));
    }

    return ms.ToArray();
  }

  private static byte[] _BuildMinimalRgbWithComment(int width, int height, string comment) {
    using var ms = new MemoryStream();
    var header = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BitConverter.SingleToInt32Bits(3.71f));
    var commentBytes = Encoding.ASCII.GetBytes(comment);
    Array.Copy(commentBytes, 0, header, 8, Math.Min(commentBytes.Length, 80));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(88), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(90), (ushort)height);
    ms.Write(header, 0, 96);

    ms.WriteByte(0);
    ms.WriteByte(8);
    ms.WriteByte(2);
    ms.WriteByte(0x40 | 0x20 | 0x10);

    var pixelCount = width * height;
    for (var i = 0; i < pixelCount; ++i) {
      ms.WriteByte(128);
      ms.WriteByte(0);
      ms.WriteByte(0);
      ms.WriteByte(0);
    }

    return ms.ToArray();
  }

  private static byte[] _BuildMinimalRgba(int width, int height) {
    using var ms = new MemoryStream();
    var header = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BitConverter.SingleToInt32Bits(3.71f));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(88), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(90), (ushort)height);
    ms.Write(header, 0, 96);

    ms.WriteByte(1);
    ms.WriteByte(8);
    ms.WriteByte(2);
    ms.WriteByte(0x80);

    ms.WriteByte(0);
    ms.WriteByte(8);
    ms.WriteByte(2);
    ms.WriteByte(0x40 | 0x20 | 0x10);

    var pixelCount = width * height;
    for (var i = 0; i < pixelCount; ++i) {
      ms.WriteByte(128);
      ms.WriteByte(0);
      ms.WriteByte(0);
      ms.WriteByte(0);
      ms.WriteByte(0xFF);
    }

    return ms.ToArray();
  }
}
