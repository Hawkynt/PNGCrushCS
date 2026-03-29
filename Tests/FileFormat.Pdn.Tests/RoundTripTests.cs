using System;
using System.IO;
using FileFormat.Pdn;

namespace FileFormat.Pdn.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bgra32_PreservesPixelData() {
    var pixelData = new byte[4 * 4 * 4]; // 4x4 BGRA32
    pixelData[0] = 0xFF; // B
    pixelData[1] = 0x00; // G
    pixelData[2] = 0x00; // R
    pixelData[3] = 0xFF; // A
    pixelData[4] = 0x00;
    pixelData[5] = 0xFF;
    pixelData[6] = 0x00;
    pixelData[7] = 0x80;

    var original = new PdnFile {
      Width = 4,
      Height = 4,
      PixelData = pixelData,
    };

    var bytes = PdnWriter.ToBytes(original);
    var restored = PdnReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DifferentSizes() {
    foreach (var (w, h) in new[] { (1, 1), (3, 5), (100, 50), (1, 100) }) {
      var pixelData = new byte[w * h * 4];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)(i * 13 % 256);

      var original = new PdnFile {
        Width = w,
        Height = h,
        PixelData = pixelData,
      };

      var bytes = PdnWriter.ToBytes(original);
      var restored = PdnReader.FromBytes(bytes);

      Assert.That(restored.Width, Is.EqualTo(w));
      Assert.That(restored.Height, Is.EqualTo(h));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PdnFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[8 * 8 * 4],
    };

    var bytes = PdnWriter.ToBytes(original);
    var restored = PdnReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[10 * 10 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PdnFile {
      Width = 10,
      Height = 10,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdn");
    try {
      var bytes = PdnWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PdnReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VersionPreserved() {
    var original = new PdnFile {
      Version = 5,
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 4],
    };

    var bytes = PdnWriter.ToBytes(original);
    var restored = PdnReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(5));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_FromRawImage() {
    var pixelData = new byte[3 * 3 * 4];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[2] = 0xCC;
    pixelData[3] = 0xDD;

    var original = new PdnFile {
      Width = 3,
      Height = 3,
      PixelData = pixelData,
    };

    var raw = PdnFile.ToRawImage(original);
    var restored = PdnFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
