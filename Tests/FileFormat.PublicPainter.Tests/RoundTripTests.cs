using System;
using System.IO;
using FileFormat.PublicPainter;

namespace FileFormat.PublicPainter.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PublicPainterFile {
      PixelData = new byte[PublicPainterFile.DecompressedSize]
    };

    var bytes = PublicPainterWriter.ToBytes(original);
    var restored = PublicPainterReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var pixelData = new byte[PublicPainterFile.DecompressedSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new PublicPainterFile {
      PixelData = pixelData
    };

    var bytes = PublicPainterWriter.ToBytes(original);
    var restored = PublicPainterReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternedData() {
    var pixelData = new byte[PublicPainterFile.DecompressedSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PublicPainterFile {
      PixelData = pixelData
    };

    var bytes = PublicPainterWriter.ToBytes(original);
    var restored = PublicPainterReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomData() {
    var rng = new Random(42);
    var pixelData = new byte[PublicPainterFile.DecompressedSize];
    rng.NextBytes(pixelData);

    var original = new PublicPainterFile {
      PixelData = pixelData
    };

    var bytes = PublicPainterWriter.ToBytes(original);
    var restored = PublicPainterReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[PublicPainterFile.DecompressedSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new PublicPainterFile {
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmp");
    try {
      var bytes = PublicPainterWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PublicPainterReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(PublicPainterFile.ImageWidth));
        Assert.That(restored.Height, Is.EqualTo(PublicPainterFile.ImageHeight));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var original = new PublicPainterFile {
      PixelData = new byte[PublicPainterFile.DecompressedSize]
    };

    var raw = PublicPainterFile.ToRawImage(original);
    var restored = PublicPainterFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(PublicPainterFile.ImageWidth));
      Assert.That(restored.Height, Is.EqualTo(PublicPainterFile.ImageHeight));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
