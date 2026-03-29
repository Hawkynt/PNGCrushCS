using System;
using System.Buffers.Binary;
using FileFormat.FullscreenKit;

namespace FileFormat.FullscreenKit.Tests;

[TestFixture]
public sealed class FullscreenKitWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PrimaryVariant_OutputIsCorrectSize() {
    var file = _CreatePrimaryFile();
    var bytes = FullscreenKitWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(FullscreenKitFile.PrimaryFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AlternateVariant_OutputIsCorrectSize() {
    var file = _CreateAlternateFile();
    var bytes = FullscreenKitWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(FullscreenKitFile.AlternateFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteCorrectly() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[1] = 0x0700;
    palette[15] = 0x0007;

    var file = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = palette,
      PixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize]
    };

    var bytes = FullscreenKitWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2)), Is.EqualTo((short)0x0700));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(30)), Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset32() {
    var pixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;

    var file = new FullscreenKitFile {
      Width = FullscreenKitFile.PrimaryWidth,
      Height = FullscreenKitFile.PrimaryHeight,
      Palette = new short[16],
      PixelData = pixelData
    };

    var bytes = FullscreenKitWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[32], Is.EqualTo(0xAA));
      Assert.That(bytes[33], Is.EqualTo(0xBB));
    });
  }

  private static FullscreenKitFile _CreatePrimaryFile() => new() {
    Width = FullscreenKitFile.PrimaryWidth,
    Height = FullscreenKitFile.PrimaryHeight,
    Palette = new short[16],
    PixelData = new byte[FullscreenKitFile.PrimaryPixelDataSize]
  };

  private static FullscreenKitFile _CreateAlternateFile() => new() {
    Width = FullscreenKitFile.AlternateWidth,
    Height = FullscreenKitFile.AlternateHeight,
    Palette = new short[16],
    PixelData = new byte[FullscreenKitFile.AlternatePixelDataSize]
  };
}
