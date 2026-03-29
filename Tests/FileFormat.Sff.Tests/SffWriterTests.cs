using System;
using System.Buffers.Binary;
using FileFormat.Sff;

namespace FileFormat.Sff.Tests;

[TestFixture]
public sealed class SffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SffWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes() {
    var file = new SffFile { Pages = [] };
    var bytes = SffWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x53)); // 'S'
    Assert.That(bytes[1], Is.EqualTo(0x66)); // 'f'
    Assert.That(bytes[2], Is.EqualTo(0x66)); // 'f'
    Assert.That(bytes[3], Is.EqualTo(0x66)); // 'f'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize() {
    var file = new SffFile { Pages = [] };
    var bytes = SffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(SffHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PageCount() {
    var file = new SffFile {
      Pages = [
        new SffPage { Width = 8, Height = 1, PixelData = new byte[1] },
        new SffPage { Width = 8, Height = 1, PixelData = new byte[1] }
      ]
    };

    var bytes = SffWriter.ToBytes(file);
    var pageCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(pageCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionPreserved() {
    var file = new SffFile { Version = 1, Pages = [] };
    var bytes = SffWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0xDE, 0xAD };
    var file = new SffFile {
      Pages = [new SffPage { Width = 8, Height = 2, PixelData = pixelData }]
    };

    var bytes = SffWriter.ToBytes(file);
    var dataOffset = SffHeader.StructSize + SffPageHeader.StructSize;

    Assert.That(bytes[dataOffset], Is.EqualTo(0xDE));
    Assert.That(bytes[dataOffset + 1], Is.EqualTo(0xAD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FirstPageOffset() {
    var file = new SffFile {
      Pages = [new SffPage { Width = 8, Height = 1, PixelData = new byte[1] }]
    };

    var bytes = SffWriter.ToBytes(file);
    var firstPageOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));

    Assert.That(firstPageOffset, Is.EqualTo(SffHeader.StructSize));
  }
}
