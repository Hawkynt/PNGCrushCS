using System;
using System.Buffers.Binary;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class QuakeSprWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdspMagic() {
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x49)); // 'I'
    Assert.That(bytes[1], Is.EqualTo(0x44)); // 'D'
    Assert.That(bytes[2], Is.EqualTo(0x53)); // 'S'
    Assert.That(bytes[3], Is.EqualTo(0x50)); // 'P'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Version1() {
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FrameDimensions() {
    var file = new QuakeSprFile {
      Width = 16,
      Height = 32,
      PixelData = new byte[16 * 32]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    var frameWidth = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(48));
    var frameHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(52));
    Assert.That(frameWidth, Is.EqualTo(16));
    Assert.That(frameHeight, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelData() {
    var pixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = pixels
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    Assert.That(bytes[56], Is.EqualTo(0x01));
    Assert.That(bytes[57], Is.EqualTo(0x02));
    Assert.That(bytes[58], Is.EqualTo(0x03));
    Assert.That(bytes[59], Is.EqualTo(0x04));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize() {
    var w = 8;
    var h = 4;
    var file = new QuakeSprFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(36 + 20 + w * h));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SpriteType_Preserved() {
    var file = new QuakeSprFile {
      Width = 1,
      Height = 1,
      SpriteType = 3,
      PixelData = [0x00]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    var spriteType = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(spriteType, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NumFrames_Preserved() {
    var file = new QuakeSprFile {
      Width = 1,
      Height = 1,
      NumFrames = 5,
      PixelData = [0x00]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    var numFrames = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24));
    Assert.That(numFrames, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SyncType_Preserved() {
    var file = new QuakeSprFile {
      Width = 1,
      Height = 1,
      SyncType = 1,
      PixelData = [0x00]
    };

    var bytes = QuakeSprWriter.ToBytes(file);

    var syncType = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32));
    Assert.That(syncType, Is.EqualTo(1));
  }
}
