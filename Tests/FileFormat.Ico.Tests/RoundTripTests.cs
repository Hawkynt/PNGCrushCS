using System;
using System.Linq;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Ico.Tests;

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
  public void RoundTrip_SinglePngEntry() {
    var pngBytes = _CreatePngBytes(16, 16);
    var original = new IcoFile {
      Images = [new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };

    var bytes = IcoWriter.ToBytes(original);
    var restored = IcoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(1));
    Assert.That(restored.Images[0].Width, Is.EqualTo(16));
    Assert.That(restored.Images[0].Height, Is.EqualTo(16));
    Assert.That(restored.Images[0].Format, Is.EqualTo(IcoImageFormat.Png));
    Assert.That(restored.Images[0].BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.Images[0].Data, Is.EqualTo(pngBytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleEntries() {
    var png16 = _CreatePngBytes(16, 16);
    var png32 = _CreatePngBytes(32, 32);
    var original = new IcoFile {
      Images = [
        new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png16 },
        new IcoImage { Width = 32, Height = 32, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = png32 }
      ]
    };

    var bytes = IcoWriter.ToBytes(original);
    var restored = IcoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(2));
    Assert.That(restored.Images[0].Width, Is.EqualTo(16));
    Assert.That(restored.Images[0].Height, Is.EqualTo(16));
    Assert.That(restored.Images[0].Data, Is.EqualTo(png16));
    Assert.That(restored.Images[1].Width, Is.EqualTo(32));
    Assert.That(restored.Images[1].Height, Is.EqualTo(32));
    Assert.That(restored.Images[1].Data, Is.EqualTo(png32));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BmpEntry() {
    // Construct a minimal 40-byte BITMAPINFOHEADER DIB for a 2x2 32bpp image
    var dibHeader = new byte[40];
    dibHeader[0] = 40; // biSize = 40 (LE)
    dibHeader[4] = 2;  // biWidth = 2 (LE)
    dibHeader[8] = 4;  // biHeight = 4 (2*2 for ICO: XOR mask height + AND mask height)
    dibHeader[12] = 1; // biPlanes = 1 (LE)
    dibHeader[14] = 32; // biBitCount = 32 (LE)

    var pixelData = new byte[2 * 2 * 4]; // 4 pixels, 32bpp BGRA
    for (var i = 0; i < 4; ++i) {
      pixelData[i * 4] = (byte)(i * 50);     // B
      pixelData[i * 4 + 1] = (byte)(i * 40); // G
      pixelData[i * 4 + 2] = (byte)(i * 30); // R
      pixelData[i * 4 + 3] = 255;            // A
    }

    var dibData = new byte[dibHeader.Length + pixelData.Length];
    Array.Copy(dibHeader, dibData, dibHeader.Length);
    Array.Copy(pixelData, 0, dibData, dibHeader.Length, pixelData.Length);

    var original = new IcoFile {
      Images = [new IcoImage { Width = 2, Height = 2, BitsPerPixel = 32, Format = IcoImageFormat.Bmp, Data = dibData }]
    };

    var bytes = IcoWriter.ToBytes(original);
    var restored = IcoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(1));
    Assert.That(restored.Images[0].Width, Is.EqualTo(2));
    Assert.That(restored.Images[0].Height, Is.EqualTo(2));
    Assert.That(restored.Images[0].Format, Is.EqualTo(IcoImageFormat.Bmp));
    Assert.That(restored.Images[0].BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.Images[0].Data, Is.EqualTo(dibData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_256x256_EncodesAsZero() {
    var pngBytes = _CreatePngBytes(256, 256);
    var original = new IcoFile {
      Images = [new IcoImage { Width = 256, Height = 256, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };

    var bytes = IcoWriter.ToBytes(original);

    // Directory entry width/height should be 0 for 256
    Assert.That(bytes[6], Is.EqualTo(0), "256-pixel width should be encoded as 0 in directory");
    Assert.That(bytes[7], Is.EqualTo(0), "256-pixel height should be encoded as 0 in directory");

    var restored = IcoReader.FromBytes(bytes);
    Assert.That(restored.Images[0].Width, Is.EqualTo(256));
    Assert.That(restored.Images[0].Height, Is.EqualTo(256));
  }
}
