using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Hpgl.Tests;

[TestFixture]
public sealed class HpglWriterTests {

  [Test]
  [Category("Unit")]
  public void Registry_ExposesHpglAsWritable() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Hpgl);

    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.SupportsWrite, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CoalescesEqualPensIntoOneRectangle() {
    var image = new RawImage {
      Width = 3,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 255,
        255, 0, 0, 255,
        0, 0, 255, 255,
      ],
    };

    var text = Encoding.Latin1.GetString(HpglWriter.ToBytes(HpglFile.FromRawImage(image)));

    Assert.That(text, Is.EqualTo(
      "IN;SP0;PU0,0;RA30,10;SP2;PU0,0;RA20,10;SP5;PU20,0;RA30,10;SP0;"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AlphaIsCompositedOntoWhitePaper() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [
        0, 0, 0, 0,
        0, 0, 0, 255,
      ],
    };

    var text = Encoding.Latin1.GetString(HpglWriter.ToBytes(HpglFile.FromRawImage(image)));

    Assert.That(text, Is.EqualTo(
      "IN;SP0;PU0,0;RA20,10;SP1;PU10,0;RA20,10;SP0;"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_LargeSourcesAreBoundedBeforeVectorisation() {
    var pixels = new byte[1024 * 4];
    for (var offset = 0; offset < pixels.Length; offset += 4) {
      pixels[offset] = 255;
      pixels[offset + 3] = 255;
    }

    var image = new RawImage {
      Width = 1024,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var text = Encoding.Latin1.GetString(HpglWriter.ToBytes(HpglFile.FromRawImage(image)));

    Assert.That(text, Does.StartWith("IN;SP0;PU0,0;RA5120,10;"));
    Assert.That(text, Does.Contain("SP2;PU0,0;RA5120,10;"));
  }

  [Test]
  [Category("Unit")]
  public void WriteThenRead_PaletteBlocksKeepTheirPens() {
    const int width = 40;
    const int height = 20;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var offset = (y * width + x) * 4;
      if (x < width / 2)
        pixels[offset] = 255;
      else
        pixels[offset + 2] = 255;
      pixels[offset + 3] = 255;
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var bytes = HpglWriter.ToBytes(HpglFile.FromRawImage(source));
    var plot = HpglReader.FromBytes(bytes);
    var rendered = HpglFile.ToRawImage(plot);
    var rgba = rendered.ToRgba32();

    var left = _Pixel(rgba, rendered.Width, rendered.Height, 0.25, 0.5);
    var right = _Pixel(rgba, rendered.Width, rendered.Height, 0.75, 0.5);

    Assert.Multiple(() => {
      Assert.That(left, Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
      Assert.That(right, Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NonFiniteParametersAreRejected() {
    var file = new HpglFile {
      Instructions = [new("PU", [double.NaN, 0], string.Empty)],
    };

    Assert.Throws<InvalidDataException>(() => HpglWriter.ToBytes(file));
  }

  private static byte[] _Pixel(byte[] rgba, int width, int height, double x, double y) {
    var px = (int)((width - 1) * x);
    var py = (int)((height - 1) * y);
    var offset = (py * width + px) * 4;
    return rgba[offset..(offset + 4)];
  }
}
