using System;
using System.Buffers.Binary;
using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class DrawItWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrawItWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectFileSize() {
    var file = new DrawItFile {
      Width = 10,
      Height = 20,
      Palette = new byte[768],
      PixelData = new byte[200]
    };

    var bytes = DrawItWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(4 + 768 + 200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new DrawItFile {
      Width = 320,
      Height = 200,
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var bytes = DrawItWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)), Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteStartsAtOffset4() {
    var palette = new byte[768];
    palette[0] = 0xAA;
    palette[1] = 0xBB;
    palette[2] = 0xCC;

    var file = new DrawItFile {
      Width = 4,
      Height = 4,
      Palette = palette,
      PixelData = new byte[16]
    };

    var bytes = DrawItWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[4], Is.EqualTo(0xAA));
      Assert.That(bytes[5], Is.EqualTo(0xBB));
      Assert.That(bytes[6], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataStartsAfterPalette() {
    var pixelData = new byte[16];
    pixelData[0] = 0xDE;
    pixelData[1] = 0xAD;

    var file = new DrawItFile {
      Width = 4,
      Height = 4,
      Palette = new byte[768],
      PixelData = pixelData
    };

    var bytes = DrawItWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[4 + 768], Is.EqualTo(0xDE));
      Assert.That(bytes[4 + 768 + 1], Is.EqualTo(0xAD));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SmallImage_CorrectSize() {
    var file = new DrawItFile {
      Width = 1,
      Height = 1,
      Palette = new byte[768],
      PixelData = new byte[1]
    };

    var bytes = DrawItWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(4 + 768 + 1));
  }
}
