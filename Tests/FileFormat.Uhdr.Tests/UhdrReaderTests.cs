using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Uhdr;

namespace FileFormat.Uhdr.Tests;

[TestFixture]
public sealed class UhdrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UhdrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UhdrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".uhdr"));
    Assert.Throws<FileNotFoundException>(() => UhdrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UhdrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => UhdrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[UhdrHeader.StructSize];
    Encoding.ASCII.GetBytes("XXXX").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
    Assert.Throws<InvalidDataException>(() => UhdrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[UhdrHeader.StructSize];
    Encoding.ASCII.GetBytes("UHDR").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 10);
    Assert.Throws<InvalidDataException>(() => UhdrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[UhdrHeader.StructSize];
    Encoding.ASCII.GetBytes("UHDR").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 10);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0);
    Assert.Throws<InvalidDataException>(() => UhdrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensionsAndPixelData() {
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var data = new byte[UhdrHeader.StructSize + pixels.Length];
    Encoding.ASCII.GetBytes("UHDR").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
    Array.Copy(pixels, 0, data, UhdrHeader.StructSize, pixels.Length);

    var result = UhdrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var pixels = new byte[] { 42, 84, 126 };
    var data = new byte[UhdrHeader.StructSize + pixels.Length];
    Encoding.ASCII.GetBytes("UHDR").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
    Array.Copy(pixels, 0, data, UhdrHeader.StructSize, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = UhdrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreservedExactly() {
    var pixels = new byte[12];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17);

    var data = new byte[UhdrHeader.StructSize + pixels.Length];
    Encoding.ASCII.GetBytes("UHDR").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
    Array.Copy(pixels, 0, data, UhdrHeader.StructSize, pixels.Length);

    var result = UhdrReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }
}
