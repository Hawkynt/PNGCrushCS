using System;
using System.Buffers.Binary;
using FileFormat.Psd;

namespace FileFormat.Psd.Tests;

[TestFixture]
public sealed class PsdWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_StartsWithPsdSignature() {
    var file = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PsdWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'8'));
    Assert.That(bytes[1], Is.EqualTo((byte)'B'));
    Assert.That(bytes[2], Is.EqualTo((byte)'P'));
    Assert.That(bytes[3], Is.EqualTo((byte)'S'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasVersion1() {
    var file = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PsdWriter.ToBytes(file);

    var version = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4));
    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectDimensions() {
    var file = new PsdFile {
      Width = 640,
      Height = 480,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[640 * 480 * 3]
    };

    var bytes = PsdWriter.ToBytes(file);

    var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(14));
    var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(18));
    Assert.That(height, Is.EqualTo(480));
    Assert.That(width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectChannelsAndDepth() {
    var file = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 4,
      Depth = 16,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[2 * 2 * 4 * 2]
    };

    var bytes = PsdWriter.ToBytes(file);

    var channels = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(12));
    var depth = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(22));
    Assert.That(channels, Is.EqualTo(4));
    Assert.That(depth, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb8_HasCorrectColorMode() {
    var file = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsdColorMode.Grayscale,
      PixelData = new byte[2 * 2]
    };

    var bytes = PsdWriter.ToBytes(file);

    var colorMode = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(24));
    Assert.That(colorMode, Is.EqualTo((short)PsdColorMode.Grayscale));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IndexedWithPalette_WritesPaletteSection() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i] = (byte)i;         // R
      palette[256 + i] = (byte)i;   // G
      palette[512 + i] = (byte)i;   // B
    }

    var file = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsdColorMode.Indexed,
      PixelData = new byte[2 * 2],
      Palette = palette
    };

    var bytes = PsdWriter.ToBytes(file);

    // Color mode data length at offset 26 should be 768
    var colorModeDataLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(26));
    Assert.That(colorModeDataLength, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedBytes_AreAllZero() {
    var file = new PsdFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[3]
    };

    var bytes = PsdWriter.ToBytes(file);

    for (var i = 6; i < 12; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Reserved byte at offset {i} should be zero.");
  }
}
