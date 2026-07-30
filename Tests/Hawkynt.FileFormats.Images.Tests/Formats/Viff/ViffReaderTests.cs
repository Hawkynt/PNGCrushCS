using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class ViffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ViffReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ViffReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".viff"));
    Assert.Throws<FileNotFoundException>(() => ViffReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ViffReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[512];
    Assert.Throws<InvalidDataException>(() => ViffReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[1024];
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => ViffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bit_ParsesDimensions() {
    var width = 4;
    var height = 3;
    var bands = 1;
    var pixelBytes = width * height * bands;
    var data = new byte[ViffHeader.StructSize + pixelBytes];

    data[0] = ViffHeader.Magic;
    data[4] = 0x02; // little-endian
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(520), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(524), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(528), (uint)bands);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(596), (uint)ViffStorageType.Byte);

    for (var i = 0; i < pixelBytes; ++i)
      data[ViffHeader.StructSize + i] = (byte)(i * 17);

    var result = ViffReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(bands));
    Assert.That(result.StorageType, Is.EqualTo(ViffStorageType.Byte));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(17));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_Parses() {
    var width = 2;
    var height = 2;
    var bands = 1;
    var pixelBytes = width * height * bands;
    var data = new byte[ViffHeader.StructSize + pixelBytes];

    data[0] = ViffHeader.Magic;
    data[4] = 0x02;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(520), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(524), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(528), (uint)bands);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(596), (uint)ViffStorageType.Byte);
    data[ViffHeader.StructSize] = 0xAA;
    data[ViffHeader.StructSize + 1] = 0xBB;
    data[ViffHeader.StructSize + 2] = 0xCC;
    data[ViffHeader.StructSize + 3] = 0xDD;

    using var ms = new MemoryStream(data);
    var result = ViffReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CommentPreserved() {
    var data = new byte[ViffHeader.StructSize + 4];
    data[0] = ViffHeader.Magic;
    data[4] = 0x02;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(520), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(524), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(528), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(596), (uint)ViffStorageType.Byte);

    var comment = "Test comment";
    var commentBytes = System.Text.Encoding.ASCII.GetBytes(comment);
    Array.Copy(commentBytes, 0, data, 8, commentBytes.Length);

    var result = ViffReader.FromBytes(data);

    Assert.That(result.Comment, Is.EqualTo(comment));
  }
}
