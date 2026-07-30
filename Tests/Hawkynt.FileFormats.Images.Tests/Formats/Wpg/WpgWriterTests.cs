using System;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

[TestFixture]
public sealed class WpgWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagicBytes() {
    var file = new WpgFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[4]
    };

    var bytes = WpgWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xFF));
      Assert.That(bytes[1], Is.EqualTo((byte)'W'));
      Assert.That(bytes[2], Is.EqualTo((byte)'P'));
      Assert.That(bytes[3], Is.EqualTo((byte)'C'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSizeIs16() {
    var file = new WpgFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 8,
      PixelData = new byte[1]
    };

    var bytes = WpgWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(WpgHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsStartAndEndRecords() {
    var file = new WpgFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[4]
    };

    var bytes = WpgWriter.ToBytes(file);

    // StartWpg record should be right after header
    Assert.That(bytes[WpgHeader.StructSize], Is.EqualTo((byte)WpgRecordType.StartWpg));

    // EndWpg should be the last record type byte (2nd to last byte, since size=0 follows)
    Assert.That(bytes[^2], Is.EqualTo((byte)WpgRecordType.EndWpg));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var file = new WpgFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = WpgWriter.ToBytes(file);
    var restored = WpgReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }
}
