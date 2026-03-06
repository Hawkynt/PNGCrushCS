using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Cur;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Cur.Tests;

[TestFixture]
public sealed class CurWriterTests {

  private static byte[] _CreatePngBytes(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(_ => new byte[width * 4]).ToArray()
    };
    return PngWriter.ToBytes(pngFile);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasCursorTypeInHeader() {
    var pngBytes = _CreatePngBytes(16, 16);
    var curFile = new CurFile {
      Images = [new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes, HotspotX = 5, HotspotY = 10 }]
    };

    var bytes = CurWriter.ToBytes(curFile);

    var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
    Assert.That(type, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HotspotInDirectory() {
    var pngBytes = _CreatePngBytes(16, 16);
    var curFile = new CurFile {
      Images = [new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes, HotspotX = 7, HotspotY = 12 }]
    };

    var bytes = CurWriter.ToBytes(curFile);

    var hotspotX = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6 + 4));
    var hotspotY = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6 + 6));
    Assert.That(hotspotX, Is.EqualTo(7));
    Assert.That(hotspotY, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CountMatchesEntries() {
    var png16 = _CreatePngBytes(16, 16);
    var png32 = _CreatePngBytes(32, 32);
    var curFile = new CurFile {
      Images = [
        new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png16, HotspotX = 0, HotspotY = 0 },
        new CurImage { Width = 32, Height = 32, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png32, HotspotX = 16, HotspotY = 16 }
      ]
    };

    var bytes = CurWriter.ToBytes(curFile);

    var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
    Assert.That(count, Is.EqualTo(2));
  }
}
