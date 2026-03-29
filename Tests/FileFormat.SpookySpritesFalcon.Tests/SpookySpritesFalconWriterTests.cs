using System;
using System.Buffers.Binary;
using FileFormat.SpookySpritesFalcon;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class SpookySpritesFalconWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithDimensionsBigEndian() {
    var file = new SpookySpritesFalconFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 2],
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndMarker() {
    var file = new SpookySpritesFalconFile {
      Width = 2,
      Height = 1,
      PixelData = new byte[2 * 1 * 2],
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(file);

    Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedSmallerThanRaw_ForRepeatData() {
    var pixelData = new byte[100 * 2];
    for (var i = 0; i < pixelData.Length; i += 2) {
      pixelData[i] = 0xF8;
      pixelData[i + 1] = 0x00;
    }

    var file = new SpookySpritesFalconFile {
      Width = 100,
      Height = 1,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(SpookySpritesFalconHeader.StructSize + pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataDecodesToOriginal() {
    var pixelData = new byte[4 * 2 * 2];
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    pixelData[2] = 0x07;
    pixelData[3] = 0xE0;
    pixelData[4] = 0x00;
    pixelData[5] = 0x1F;
    pixelData[6] = 0xFF;
    pixelData[7] = 0xFF;
    // Second row
    pixelData[8] = 0xAA;
    pixelData[9] = 0xBB;
    pixelData[10] = 0xCC;
    pixelData[11] = 0xDD;
    pixelData[12] = 0x11;
    pixelData[13] = 0x22;
    pixelData[14] = 0x33;
    pixelData[15] = 0x44;

    var file = new SpookySpritesFalconFile {
      Width = 4,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(file);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }
}
