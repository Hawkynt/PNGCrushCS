using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Gaf;

namespace FileFormat.Gaf.Tests;

[TestFixture]
public sealed class GafWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GafWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytesCorrect() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "test",
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    Assert.That(magic, Is.EqualTo(0x00010100u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EntryCountIsOne() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "test",
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(file);

    var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(entryCount, Is.EqualTo(1u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NamePreserved() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "tank_turret",
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(file);

    // Entry header starts at offset pointed to by the entry pointer
    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var nameSpan = bytes.AsSpan(entryOffset + 8, 32);
    var nullIdx = nameSpan.IndexOf((byte)0);
    var name = Encoding.ASCII.GetString(nameSpan[..(nullIdx < 0 ? 32 : nullIdx)]);
    Assert.That(name, Is.EqualTo("tank_turret"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FrameDimensionsCorrect() {
    var file = new GafFile {
      Width = 16,
      Height = 32,
      Name = "unit",
      PixelData = new byte[16 * 32],
    };

    var bytes = GafWriter.ToBytes(file);

    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var framePointerOffset = entryOffset + 40;
    var frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framePointerOffset));

    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 2));

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TransparencyIndexPreserved() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "test",
      TransparencyIndex = 42,
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(file);

    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var framePointerOffset = entryOffset + 40;
    var frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framePointerOffset));

    Assert.That(bytes[frameOffset + 8], Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent() {
    var pixelData = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "test",
      PixelData = pixelData,
    };

    var bytes = GafWriter.ToBytes(file);

    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var framePointerOffset = entryOffset + 40;
    var frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framePointerOffset));
    var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(frameOffset + 12));

    Assert.That(bytes[dataOffset], Is.EqualTo(0x10));
    Assert.That(bytes[dataOffset + 1], Is.EqualTo(0x20));
    Assert.That(bytes[dataOffset + 2], Is.EqualTo(0x30));
    Assert.That(bytes[dataOffset + 3], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UncompressedFlag() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      Name = "test",
      PixelData = new byte[4],
    };

    var bytes = GafWriter.ToBytes(file);

    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var framePointerOffset = entryOffset + 40;
    var frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framePointerOffset));

    Assert.That(bytes[frameOffset + 9], Is.EqualTo(0)); // compressed = 0
  }
}
