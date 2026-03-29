using System;
using System.Buffers.Binary;
using FileFormat.Psb;

namespace FileFormat.Psb.Tests;

[TestFixture]
public sealed class PsbWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsbWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_StartsWithPsbSignature() {
    var file = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PsbWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'8'));
    Assert.That(bytes[1], Is.EqualTo((byte)'B'));
    Assert.That(bytes[2], Is.EqualTo((byte)'P'));
    Assert.That(bytes[3], Is.EqualTo((byte)'S'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasVersion2() {
    var file = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PsbWriter.ToBytes(file);

    var version = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4));
    Assert.That(version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectDimensions() {
    var file = new PsbFile {
      Width = 640,
      Height = 480,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[640 * 480 * 3]
    };

    var bytes = PsbWriter.ToBytes(file);

    var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(14));
    var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(18));
    Assert.That(height, Is.EqualTo(480));
    Assert.That(width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectChannelsAndDepth() {
    var file = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 4,
      Depth = 16,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[2 * 2 * 4 * 2]
    };

    var bytes = PsbWriter.ToBytes(file);

    var channels = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(12));
    var depth = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(22));
    Assert.That(channels, Is.EqualTo(4));
    Assert.That(depth, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectColorMode() {
    var file = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsbColorMode.Grayscale,
      PixelData = new byte[2 * 2]
    };

    var bytes = PsbWriter.ToBytes(file);

    var colorMode = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(24));
    Assert.That(colorMode, Is.EqualTo((short)PsbColorMode.Grayscale));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IndexedWithPalette_WritesPaletteSection() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i] = (byte)i;
      palette[256 + i] = (byte)i;
      palette[512 + i] = (byte)i;
    }

    var file = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsbColorMode.Indexed,
      PixelData = new byte[2 * 2],
      Palette = palette
    };

    var bytes = PsbWriter.ToBytes(file);

    var colorModeDataLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(26));
    Assert.That(colorModeDataLength, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedBytes_AreAllZero() {
    var file = new PsbFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[3]
    };

    var bytes = PsbWriter.ToBytes(file);

    for (var i = 6; i < 12; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Reserved byte at offset {i} should be zero.");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LayerMaskInfoSection_Uses8ByteLength() {
    var file = new PsbFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[3]
    };

    var bytes = PsbWriter.ToBytes(file);

    // After header (26) + color mode data length (4) + image resources length (4) = offset 34
    // Layer/mask info length should be at offset 34 and be 8 bytes
    var layerMaskInfoLength = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(34));
    Assert.That(layerMaskInfoLength, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LayerMaskInfoSection_WritesDataAfter8ByteLength() {
    var layerMaskInfo = new byte[] { 0x01, 0x02, 0x03, 0x04 };

    var file = new PsbFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[3],
      LayerMaskInfo = layerMaskInfo
    };

    var bytes = PsbWriter.ToBytes(file);

    var length = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(34));
    Assert.That(length, Is.EqualTo(4));

    // Data starts at offset 42 (34 + 8)
    Assert.That(bytes[42], Is.EqualTo(0x01));
    Assert.That(bytes[43], Is.EqualTo(0x02));
    Assert.That(bytes[44], Is.EqualTo(0x03));
    Assert.That(bytes[45], Is.EqualTo(0x04));
  }
}
