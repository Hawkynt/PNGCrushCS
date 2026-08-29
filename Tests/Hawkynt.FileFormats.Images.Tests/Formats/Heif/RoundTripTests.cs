using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static RawImage _Smooth(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(24 + 200L * x / Math.Max(1, width - 1));
        pixels[at + 1] = (byte)(32 + 176L * y / Math.Max(1, height - 1));
        pixels[at + 2] = (byte)(40 + 160L * (x + y) / Math.Max(1, width + height - 2));
      }
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _Mae(byte[] expected, byte[] actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    long total = 0;
    for (var i = 0; i < expected.Length; ++i)
      total += Math.Abs(expected[i] - actual[i]);
    return total / (double)expected.Length;
  }

  [Test]
  [Category("Integration")]
  public void Writer_EmitsAddressedHvc1ItemAndDecodesThroughPcmCabacPath() {
    var source = _Smooth(96, 80);
    var bytes = HeifWriter.ToBytes(HeifFile.FromRawImage(source));
    var text = Encoding.Latin1.GetString(bytes);
    var restoredFile = HeifReader.FromBytes(bytes);
    var restored = HeifFile.ToRawImage(restoredFile);
    var mae = _Mae(source.PixelData, restored.PixelData);

    Assert.Multiple(() => {
      Assert.That(text, Does.Contain("ftypheic"));
      Assert.That(text, Does.Contain("hvc1"));
      Assert.That(text, Does.Contain("hvcC"));
      Assert.That(text, Does.Contain("iloc"));
      Assert.That(restoredFile.Images, Has.Count.EqualTo(1));
      Assert.That(restoredFile.Images[0].ItemType, Is.EqualTo("hvc1"));
      Assert.That(restoredFile.RawImageData, Is.Not.EqualTo(source.PixelData),
        "the item payload must be a coded HEVC sample, not the historical raw-RGB mdat fallback");
      Assert.That(restored.Width, Is.EqualTo(source.Width));
      Assert.That(restored.Height, Is.EqualTo(source.Height));
      Assert.That(mae, Is.LessThan(6.0), $"8-bit 4:2:0 PCM round-trip RGB MAE was {mae:F2}");
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_OddDimensionsUseCleanApertureAndReturnRequestedExtent() {
    var source = _Smooth(65, 67);
    var bytes = HeifWriter.ToBytes(HeifFile.FromRawImage(source));
    var text = Encoding.Latin1.GetString(bytes);
    var info = HeifReader.ReadImageInfo(bytes);
    var restored = HeifFile.ToRawImage(HeifReader.FromBytes(bytes));
    var mae = _Mae(source.PixelData, restored.PixelData);

    Assert.Multiple(() => {
      Assert.That(text, Does.Contain("clap"));
      Assert.That(info, Is.Not.Null);
      Assert.That(info!.Value.Width, Is.EqualTo(source.Width));
      Assert.That(info.Value.Height, Is.EqualTo(source.Height));
      Assert.That(restored.Width, Is.EqualTo(source.Width));
      Assert.That(restored.Height, Is.EqualTo(source.Height));
      Assert.That(mae, Is.LessThan(7.0), $"odd-size HEIF round-trip RGB MAE was {mae:F2}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var source = _Smooth(64, 64);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".heic");
    try {
      File.WriteAllBytes(tempPath, HeifWriter.ToBytes(HeifFile.FromRawImage(source)));
      var restored = HeifFile.ToRawImage(HeifReader.FromFile(new FileInfo(tempPath)));
      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(source.Width));
        Assert.That(restored.Height, Is.EqualTo(source.Height));
        Assert.That(_Mae(source.PixelData, restored.PixelData), Is.LessThan(6.0));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BrandPreserved() {
    var source = _Smooth(64, 64);
    var file = HeifFile.FromRawImage(source) with { Brand = "heix" };
    var restored = HeifReader.FromBytes(HeifWriter.ToBytes(file));
    Assert.That(restored.Brand, Is.EqualTo("heix"));
  }
}
