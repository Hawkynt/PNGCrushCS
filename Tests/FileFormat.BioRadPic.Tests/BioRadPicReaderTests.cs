using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.BioRadPic;

namespace FileFormat.BioRadPic.Tests;

[TestFixture]
public sealed class BioRadPicReaderTests {

  private static byte[] BuildValidPic(ushort nx, ushort ny, bool isByte, byte fillValue = 0x00) {
    var bytesPerPixel = isByte ? 1 : 2;
    var pixelSize = nx * ny * bytesPerPixel;
    var data = new byte[BioRadPicHeader.StructSize + pixelSize];
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt16LittleEndian(span[0..], nx);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], ny);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1); // npic
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], isByte ? (short)1 : (short)0);
    BinaryPrimitives.WriteUInt16LittleEndian(span[54..], BioRadPicHeader.MagicFileId);

    for (var i = BioRadPicHeader.StructSize; i < data.Length; ++i)
      data[i] = fillValue;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic"));
    Assert.Throws<FileNotFoundException>(() => BioRadPicReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[40];
    Assert.Throws<InvalidDataException>(() => BioRadPicReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFileId_ThrowsInvalidDataException() {
    var data = new byte[BioRadPicHeader.StructSize + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54), 9999); // wrong magic

    Assert.Throws<InvalidDataException>(() => BioRadPicReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8Bit() {
    var data = BuildValidPic(4, 3, true, 0xAB);

    var result = BioRadPicReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.NumImages, Is.EqualTo(1));
    Assert.That(result.ByteFormat, Is.True);
    Assert.That(result.PixelData.Length, Is.EqualTo(12));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid16Bit() {
    var data = BuildValidPic(4, 3, false);
    // Write a known 16-bit LE value at pixel 0
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(BioRadPicHeader.StructSize), 1000);

    var result = BioRadPicReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.ByteFormat, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(24));
    var px0 = BinaryPrimitives.ReadUInt16LittleEndian(result.PixelData.AsSpan(0));
    Assert.That(px0, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = BuildValidPic(2, 2, true, 0xCD);

    using var ms = new MemoryStream(data);
    var result = BioRadPicReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesName() {
    var data = BuildValidPic(2, 2, true);
    // Write "test" at offset 18
    data[18] = (byte)'t';
    data[19] = (byte)'e';
    data[20] = (byte)'s';
    data[21] = (byte)'t';
    data[22] = 0;

    var result = BioRadPicReader.FromBytes(data);

    Assert.That(result.Name, Is.EqualTo("test"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesLens() {
    var data = BuildValidPic(2, 2, true);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(64), 40);

    var result = BioRadPicReader.FromBytes(data);

    Assert.That(result.Lens, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesMagFactor() {
    var data = BuildValidPic(2, 2, true);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(66), 2.5f);

    var result = BioRadPicReader.FromBytes(data);

    Assert.That(result.MagFactor, Is.EqualTo(2.5f));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InsufficientPixelData_ThrowsInvalidDataException() {
    // Header says 100x100 but only provide 76 bytes total (header only)
    var data = new byte[BioRadPicHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 100);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 100);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54), BioRadPicHeader.MagicFileId);

    Assert.Throws<InvalidDataException>(() => BioRadPicReader.FromBytes(data));
  }
}
