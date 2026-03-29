using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Qrt;

namespace FileFormat.Qrt.Tests;

[TestFixture]
public sealed class QrtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QrtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QrtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".qrt"));
    Assert.Throws<FileNotFoundException>(() => QrtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QrtReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => QrtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[QrtHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 10);
    Assert.Throws<InvalidDataException>(() => QrtReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var data = new byte[QrtHeader.StructSize + pixels.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    Array.Copy(pixels, 0, data, QrtHeader.StructSize, pixels.Length);

    var result = QrtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var pixels = new byte[] { 42, 84, 126 };
    var data = new byte[QrtHeader.StructSize + pixels.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    Array.Copy(pixels, 0, data, QrtHeader.StructSize, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = QrtReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }
}
