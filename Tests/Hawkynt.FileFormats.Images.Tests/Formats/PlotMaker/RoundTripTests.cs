using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PlotMaker;

namespace FileFormat.PlotMaker.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteRead_AllFieldsPreserved() {
    var pixelData = new byte[32];
    for (var i = 0; i < 32; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData,
    };

    var bytes = PlotMakerWriter.ToBytes(original);
    var restored = PlotMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PlotMakerFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[8],
    };

    var bytes = PlotMakerWriter.ToBytes(original);
    var restored = PlotMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var pixelData = new byte[32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData,
    };

    var bytes = PlotMakerWriter.ToBytes(original);
    var restored = PlotMakerReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[32];
    pixelData[0] = 0xAA;
    pixelData[15] = 0x55;
    pixelData[31] = 0xFF;

    var original = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".plt");
    try {
      var bytes = PlotMakerWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PlotMakerReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_NonByteBoundaryWidth() {
    // width=10 -> bytesPerRow=2
    var pixelData = new byte[2 * 5]; // 5 rows
    pixelData[0] = 0xAA;
    pixelData[1] = 0xC0;

    var original = new PlotMakerFile {
      Width = 10,
      Height = 5,
      PixelData = pixelData,
    };

    var bytes = PlotMakerWriter.ToBytes(original);
    var restored = PlotMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(10));
    Assert.That(restored.Height, Is.EqualTo(5));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[32];
    pixelData[0] = 0xFF; // first 8 pixels set
    pixelData[2] = 0xAA; // alternating pixels

    var original = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData,
    };

    var raw = PlotMakerFile.ToRawImage(original);
    var restored = PlotMakerFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new PlotMakerFile {
      Width = 8,
      Height = 4,
      PixelData = new byte[4],
    };

    var raw = PlotMakerFile.ToRawImage(original);
    var restored = PlotMakerFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var pixelData = new byte[32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData,
    };

    var raw = PlotMakerFile.ToRawImage(original);
    var restored = PlotMakerFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var original = new PlotMakerFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[] { 0xAA, 0x55 },
    };

    var bytes = PlotMakerWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = PlotMakerReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
