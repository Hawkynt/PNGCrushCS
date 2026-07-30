using System;
using System.IO;
using FileFormat.Dcx;
using FileFormat.Pcx;

namespace FileFormat.Dcx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePage() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11);

    var page = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var original = new DcxFile { Pages = [page] };

    var bytes = DcxWriter.ToBytes(original);
    var restored = DcxReader.FromBytes(bytes);

    Assert.That(restored.Pages.Count, Is.EqualTo(1));
    Assert.That(restored.Pages[0].Width, Is.EqualTo(4));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(4));
    Assert.That(restored.Pages[0].ColorMode, Is.EqualTo(PcxColorMode.Rgb24));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage() {
    var pixelData1 = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData1.Length; ++i)
      pixelData1[i] = (byte)(i * 3);

    var pixelData2 = new byte[8 * 8 * 3];
    for (var i = 0; i < pixelData2.Length; ++i)
      pixelData2[i] = (byte)(i * 7);

    var page1 = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData1,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var page2 = new PcxFile {
      Width = 8,
      Height = 8,
      BitsPerPixel = 8,
      PixelData = pixelData2,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var original = new DcxFile { Pages = [page1, page2] };

    var bytes = DcxWriter.ToBytes(original);
    var restored = DcxReader.FromBytes(bytes);

    Assert.That(restored.Pages.Count, Is.EqualTo(2));
    Assert.That(restored.Pages[0].Width, Is.EqualTo(4));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(4));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData1));
    Assert.That(restored.Pages[1].Width, Is.EqualTo(8));
    Assert.That(restored.Pages[1].Height, Is.EqualTo(8));
    Assert.That(restored.Pages[1].PixelData, Is.EqualTo(pixelData2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13);

    var page = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var original = new DcxFile { Pages = [page] };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dcx");

    try {
      var bytes = DcxWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DcxReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Pages.Count, Is.EqualTo(1));
      Assert.That(restored.Pages[0].Width, Is.EqualTo(4));
      Assert.That(restored.Pages[0].PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyPages() {
    var original = new DcxFile { Pages = [] };

    var bytes = DcxWriter.ToBytes(original);
    var restored = DcxReader.FromBytes(bytes);

    Assert.That(restored.Pages.Count, Is.EqualTo(0));
  }
}
