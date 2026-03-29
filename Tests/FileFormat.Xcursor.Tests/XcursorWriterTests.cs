using System;
using System.Buffers.Binary;
using FileFormat.Xcursor;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class XcursorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithXcurMagic() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'X'));
    Assert.That(bytes[1], Is.EqualTo((byte)'c'));
    Assert.That(bytes[2], Is.EqualTo((byte)'u'));
    Assert.That(bytes[3], Is.EqualTo((byte)'r'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSizeIs16() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(headerSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NtocIsOne() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var ntoc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));

    Assert.That(ntoc, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TocEntryHasImageChunkType() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));

    Assert.That(type, Is.EqualTo(0xFFFD0002));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TocEntryHasCorrectNominalSize() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 48,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var subtype = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));

    Assert.That(subtype, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageChunkHasCorrectDimensions() {
    var file = new XcursorFile {
      Width = 16,
      Height = 24,
      NominalSize = 32,
      PixelData = new byte[16 * 24 * 4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var chunkStart = 16 + 12;
    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart + 16));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart + 20));

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageChunkHasCorrectHotspot() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      XHot = 3,
      YHot = 7,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var chunkStart = 16 + 12;
    var xhot = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart + 24));
    var yhot = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart + 28));

    Assert.That(xhot, Is.EqualTo(3));
    Assert.That(yhot, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageChunkHasCorrectDelay() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      Delay = 200,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var chunkStart = 16 + 12;
    var delay = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart + 32));

    Assert.That(delay, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeIsCorrect() {
    var file = new XcursorFile {
      Width = 2,
      Height = 3,
      NominalSize = 32,
      PixelData = new byte[2 * 3 * 4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var expectedSize = 16 + 12 + 36 + 2 * 3 * 4;

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = pixels
    };

    var bytes = XcursorWriter.ToBytes(file);
    var pixelStart = 16 + 12 + 36;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[pixelStart + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageChunkHeaderSizeIs36() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = new byte[4]
    };

    var bytes = XcursorWriter.ToBytes(file);
    var chunkStart = 16 + 12;
    var chunkHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chunkStart));

    Assert.That(chunkHeaderSize, Is.EqualTo(36));
  }
}
