using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.AliasPix;

namespace FileFormat.AliasPix.Tests;

[TestFixture]
public sealed class AliasPixReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AliasPixReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AliasPixReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pix"));
    Assert.Throws<FileNotFoundException>(() => AliasPixReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[6];
    Assert.Throws<InvalidDataException>(() => AliasPixReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid24bpp() {
    // Build a minimal 2x1 24bpp PIX file: header + RLE packets
    var header = new byte[AliasPixHeader.StructSize];
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0), 2);  // Width
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 1);  // Height
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 0);  // XOffset
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0);  // YOffset
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), 24); // BitsPerPixel

    // Two pixels: run of 2 with B=0x10, G=0x20, R=0x30
    var rle = new byte[] { 2, 0x10, 0x20, 0x30 };

    var data = new byte[header.Length + rle.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(rle, 0, data, header.Length, rle.Length);

    var result = AliasPixReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    Assert.That(result.PixelData.Length, Is.EqualTo(6));
    Assert.That(result.PixelData[0], Is.EqualTo(0x10)); // B
    Assert.That(result.PixelData[1], Is.EqualTo(0x20)); // G
    Assert.That(result.PixelData[2], Is.EqualTo(0x30)); // R
    Assert.That(result.PixelData[3], Is.EqualTo(0x10)); // B (second pixel)
    Assert.That(result.PixelData[4], Is.EqualTo(0x20)); // G
    Assert.That(result.PixelData[5], Is.EqualTo(0x30)); // R
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBpp_ThrowsInvalidDataException() {
    var header = new byte[AliasPixHeader.StructSize];
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(0), 1);  // Width
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 1);  // Height
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 0);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), 16); // Invalid BPP

    Assert.Throws<InvalidDataException>(() => AliasPixReader.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AliasPixReader.FromStream(null!));
  }
}
