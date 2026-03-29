using System;
using System.Buffers.Binary;
using FileFormat.AmigaIcon;

namespace FileFormat.AmigaIcon.Tests;

[TestFixture]
public sealed class AmigaIconWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmigaIconWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = _Create16x8Depth2();

    var bytes = AmigaIconWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xE3));
    Assert.That(bytes[1], Is.EqualTo(0x10));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderDimensionsCorrect() {
    var file = _Create16x8Depth2();

    var bytes = AmigaIconWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(10));
    var height = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(12));
    var depth = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(14));

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(8));
    Assert.That(depth, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageDataPointerNonZero() {
    var file = _Create16x8Depth2();

    var bytes = AmigaIconWriter.ToBytes(file);

    var ptr = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16));
    Assert.That(ptr, Is.Not.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IconTypePreserved() {
    var file = new AmigaIconFile {
      Width = 16,
      Height = 8,
      Depth = 1,
      IconType = (int)AmigaIconType.Disk,
      PlanarData = new byte[AmigaIconFile.PlanarDataSize(16, 8, 1)],
    };

    var bytes = AmigaIconWriter.ToBytes(file);

    Assert.That(bytes[54], Is.EqualTo((byte)AmigaIconType.Disk));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = _Create16x8Depth2();

    var bytes = AmigaIconWriter.ToBytes(file);
    var expectedSize = AmigaIconHeader.StructSize + AmigaIconFile.PlanarDataSize(16, 8, 2);

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PlanarDataPreserved() {
    var planarSize = AmigaIconFile.PlanarDataSize(16, 8, 2);
    var planarData = new byte[planarSize];
    planarData[0] = 0xDE;
    planarData[1] = 0xAD;

    var file = new AmigaIconFile {
      Width = 16,
      Height = 8,
      Depth = 2,
      PlanarData = planarData,
    };

    var bytes = AmigaIconWriter.ToBytes(file);

    Assert.That(bytes[AmigaIconHeader.StructSize], Is.EqualTo(0xDE));
    Assert.That(bytes[AmigaIconHeader.StructSize + 1], Is.EqualTo(0xAD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PlanePickMatchesDepth() {
    var file = new AmigaIconFile {
      Width = 16,
      Height = 8,
      Depth = 3,
      PlanarData = new byte[AmigaIconFile.PlanarDataSize(16, 8, 3)],
    };

    var bytes = AmigaIconWriter.ToBytes(file);
    var planePick = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(18));

    Assert.That(planePick, Is.EqualTo(0b111));
  }

  private static AmigaIconFile _Create16x8Depth2() => new() {
    Width = 16,
    Height = 8,
    Depth = 2,
    PlanarData = new byte[AmigaIconFile.PlanarDataSize(16, 8, 2)],
  };
}
