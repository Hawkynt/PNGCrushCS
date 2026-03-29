using System;
using System.Buffers.Binary;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

[TestFixture]
public sealed class WmfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => WmfWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Magic() {
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x00]
    };

    var bytes = WmfWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xD7));
    Assert.That(bytes[1], Is.EqualTo(0xCD));
    Assert.That(bytes[2], Is.EqualTo(0xC6));
    Assert.That(bytes[3], Is.EqualTo(0x9A));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ChecksumValid() {
    var file = new WmfFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = WmfWriter.ToBytes(file);
    var span = bytes.AsSpan();

    ushort xor = 0;
    for (var i = 0; i < 10; ++i)
      xor ^= BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]);

    var storedChecksum = BinaryPrimitives.ReadUInt16LittleEndian(span[20..]);
    Assert.That(storedChecksum, Is.EqualTo(xor));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StandardHeaderVersion() {
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = [0, 0, 0]
    };

    var bytes = WmfWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26));

    Assert.That(version, Is.EqualTo(0x0300));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MetaEofPresent() {
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = [0, 0, 0]
    };

    var bytes = WmfWriter.ToBytes(file);

    // The last 6 bytes should be META_EOF: size=3 (uint32 LE), function=0 (uint16 LE)
    var eofSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 6));
    var eofFunction = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2));

    Assert.That(eofSize, Is.EqualTo(3u));
    Assert.That(eofFunction, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize() {
    var w = 2;
    var h = 2;
    var file = new WmfFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 3]
    };

    var bytes = WmfWriter.ToBytes(file);

    // File must be at least placeable(22) + standard(18) + stretchDIB record + EOF(6)
    Assert.That(bytes.Length, Is.GreaterThan(22 + 18 + 6));
  }
}
