using System;
using System.Linq;
using FileFormat.Cur;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Cur.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static byte[] _CreatePngBytes(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(y => {
        var row = new byte[width * 4];
        for (var x = 0; x < width; ++x) {
          row[x * 4] = (byte)((x + y) * 7);
          row[x * 4 + 1] = (byte)((x + y) * 11);
          row[x * 4 + 2] = (byte)((x + y) * 13);
          row[x * 4 + 3] = 255;
        }
        return row;
      }).ToArray()
    };
    return PngWriter.ToBytes(pngFile);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleEntry_PreservesHotspot() {
    var pngBytes = _CreatePngBytes(16, 16);
    var original = new CurFile {
      Images = [new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes, HotspotX = 5, HotspotY = 10 }]
    };

    var bytes = CurWriter.ToBytes(original);
    var restored = CurReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(1));
    Assert.That(restored.Images[0].Width, Is.EqualTo(16));
    Assert.That(restored.Images[0].Height, Is.EqualTo(16));
    Assert.That(restored.Images[0].Format, Is.EqualTo(IcoImageFormat.Png));
    Assert.That(restored.Images[0].HotspotX, Is.EqualTo(5));
    Assert.That(restored.Images[0].HotspotY, Is.EqualTo(10));
    Assert.That(restored.Images[0].Data, Is.EqualTo(pngBytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleEntries() {
    var png16 = _CreatePngBytes(16, 16);
    var png32 = _CreatePngBytes(32, 32);
    var original = new CurFile {
      Images = [
        new CurImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png16, HotspotX = 3, HotspotY = 7 },
        new CurImage { Width = 32, Height = 32, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png32, HotspotX = 16, HotspotY = 16 }
      ]
    };

    var bytes = CurWriter.ToBytes(original);
    var restored = CurReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(2));
    Assert.That(restored.Images[0].Width, Is.EqualTo(16));
    Assert.That(restored.Images[0].HotspotX, Is.EqualTo(3));
    Assert.That(restored.Images[0].HotspotY, Is.EqualTo(7));
    Assert.That(restored.Images[0].Data, Is.EqualTo(png16));
    Assert.That(restored.Images[1].Width, Is.EqualTo(32));
    Assert.That(restored.Images[1].HotspotX, Is.EqualTo(16));
    Assert.That(restored.Images[1].HotspotY, Is.EqualTo(16));
    Assert.That(restored.Images[1].Data, Is.EqualTo(png32));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_256x256_EncodesAsZero() {
    var pngBytes = _CreatePngBytes(256, 256);
    var original = new CurFile {
      Images = [new CurImage { Width = 256, Height = 256, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes, HotspotX = 128, HotspotY = 128 }]
    };

    var bytes = CurWriter.ToBytes(original);

    // Directory entry width/height should be 0 for 256
    Assert.That(bytes[6], Is.EqualTo(0), "256-pixel width should be encoded as 0 in directory");
    Assert.That(bytes[7], Is.EqualTo(0), "256-pixel height should be encoded as 0 in directory");

    var restored = CurReader.FromBytes(bytes);
    Assert.That(restored.Images[0].Width, Is.EqualTo(256));
    Assert.That(restored.Images[0].Height, Is.EqualTo(256));
    Assert.That(restored.Images[0].HotspotX, Is.EqualTo(128));
    Assert.That(restored.Images[0].HotspotY, Is.EqualTo(128));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PngEntry_DataPreserved() {
    var pngBytes = _CreatePngBytes(32, 32);
    var original = new CurFile {
      Images = [new CurImage { Width = 32, Height = 32, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes, HotspotX = 0, HotspotY = 0 }]
    };

    var bytes = CurWriter.ToBytes(original);
    var restored = CurReader.FromBytes(bytes);

    Assert.That(restored.Images[0].Data, Is.EqualTo(pngBytes));
    Assert.That(restored.Images[0].BitsPerPixel, Is.EqualTo(32));
  }
}
