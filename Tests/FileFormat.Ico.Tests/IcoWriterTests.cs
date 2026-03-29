using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Ico.Tests;

[TestFixture]
public sealed class IcoWriterTests {

  private static byte[] _CreatePngBytes(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(_ => new byte[width * 4]).ToArray()
    };
    return PngWriter.ToBytes(pngFile);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleEntry_HasCorrectHeader() {
    var pngBytes = _CreatePngBytes(16, 16);
    var icoFile = new IcoFile {
      Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };

    var bytes = IcoWriter.ToBytes(icoFile);

    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
    var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
    var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(reserved, Is.EqualTo(0));
    Assert.That(type, Is.EqualTo(1));
    Assert.That(count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleEntries_CountCorrect() {
    var png16 = _CreatePngBytes(16, 16);
    var png32 = _CreatePngBytes(32, 32);
    var icoFile = new IcoFile {
      Images = [
        new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png16 },
        new IcoImage { Width = 32, Height = 32, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png32 }
      ]
    };

    var bytes = IcoWriter.ToBytes(icoFile);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PngEntry_DataPreserved() {
    var pngBytes = _CreatePngBytes(16, 16);
    var icoFile = new IcoFile {
      Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };

    var bytes = IcoWriter.ToBytes(icoFile);
    var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6 + 12));
    var dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6 + 8));
    var embedded = bytes.AsSpan(dataOffset, dataSize).ToArray();

    Assert.That(embedded, Is.EqualTo(pngBytes));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EntryDimensionsInDirectory() {
    var pngBytes = _CreatePngBytes(48, 48);
    var icoFile = new IcoFile {
      Images = [new IcoImage { Width = 48, Height = 48, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };

    var bytes = IcoWriter.ToBytes(icoFile);

    Assert.That(bytes[6], Is.EqualTo(48), "Width byte in directory entry");
    Assert.That(bytes[7], Is.EqualTo(48), "Height byte in directory entry");
  }
}
