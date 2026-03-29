using System;
using System.IO;
using FileFormat.Sff;

namespace FileFormat.Sff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePage() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31);

    var original = new SffFile {
      Version = 1,
      Pages = [new SffPage { Width = width, Height = height, HResolution = 0, VResolution = 1, PixelData = pixelData }]
    };

    var bytes = SffWriter.ToBytes(original);
    var restored = SffReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(1));
    Assert.That(restored.Pages.Count, Is.EqualTo(1));
    Assert.That(restored.Pages[0].Width, Is.EqualTo(width));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(height));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage() {
    var pixelData1 = new byte[] { 0xFF, 0x00, 0xAA, 0x55 };
    var pixelData2 = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };

    var original = new SffFile {
      Version = 1,
      Pages = [
        new SffPage { Width = 8, Height = 4, PixelData = pixelData1 },
        new SffPage { Width = 16, Height = 3, PixelData = pixelData2 }
      ]
    };

    var bytes = SffWriter.ToBytes(original);
    var restored = SffReader.FromBytes(bytes);

    Assert.That(restored.Pages.Count, Is.EqualTo(2));
    Assert.That(restored.Pages[0].Width, Is.EqualTo(8));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(4));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData1));
    Assert.That(restored.Pages[1].Width, Is.EqualTo(16));
    Assert.That(restored.Pages[1].Height, Is.EqualTo(3));
    Assert.That(restored.Pages[1].PixelData, Is.EqualTo(pixelData2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PageDimensions_PreservedExactly() {
    var original = new SffFile {
      Version = 1,
      Pages = [new SffPage { Width = 1728, Height = 1145, HResolution = 3, VResolution = 7, PixelData = new byte[(1728 + 7) / 8 * 1145] }]
    };

    var bytes = SffWriter.ToBytes(original);
    var restored = SffReader.FromBytes(bytes);

    Assert.That(restored.Pages[0].Width, Is.EqualTo(1728));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(1145));
    Assert.That(restored.Pages[0].HResolution, Is.EqualTo(3));
    Assert.That(restored.Pages[0].VResolution, Is.EqualTo(7));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyPages() {
    var original = new SffFile { Version = 1, Pages = [] };

    var bytes = SffWriter.ToBytes(original);
    var restored = SffReader.FromBytes(bytes);

    Assert.That(restored.Pages.Count, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
    var original = new SffFile {
      Version = 1,
      Pages = [new SffPage { Width = 16, Height = 2, PixelData = pixelData }]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sff");
    try {
      var bytes = SffWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SffReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Pages.Count, Is.EqualTo(1));
      Assert.That(restored.Pages[0].Width, Is.EqualTo(16));
      Assert.That(restored.Pages[0].Height, Is.EqualTo(2));
      Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
