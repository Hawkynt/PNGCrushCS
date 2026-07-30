using System;
using System.IO;
using FileFormat.Hrz;

namespace FileFormat.Hrz.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPixelValues() {
    var pixelData = new byte[184320];
    // Set first pixel to red (R=255, G=0, B=0)
    pixelData[0] = 255;
    pixelData[1] = 0;
    pixelData[2] = 0;
    // Set second pixel to green (R=0, G=255, B=0)
    pixelData[3] = 0;
    pixelData[4] = 255;
    pixelData[5] = 0;
    // Set last pixel to blue (R=0, G=0, B=255)
    pixelData[184317] = 0;
    pixelData[184318] = 0;
    pixelData[184319] = 255;

    var original = new HrzFile {
      PixelData = pixelData
    };

    var bytes = HrzWriter.ToBytes(original);
    var restored = HrzReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new HrzFile {
      PixelData = new byte[184320]
    };

    var bytes = HrzWriter.ToBytes(original);
    var restored = HrzReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(240));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[184320];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new HrzFile {
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hrz");
    try {
      var bytes = HrzWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = HrzReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
