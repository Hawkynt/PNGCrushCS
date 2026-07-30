using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PortfolioGraphics;

namespace FileFormat.PortfolioGraphics.Tests;

[TestFixture]
public sealed class PortfolioGraphicsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PortfolioGraphicsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pgf"));
    Assert.Throws<FileNotFoundException>(() => PortfolioGraphicsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PortfolioGraphicsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPgf_Parses() {
    var data = new byte[3848];
    data[8] = 0xAB;
    data[1927] = 0xCD;

    var result = PortfolioGraphicsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(240));
    Assert.That(result.Height, Is.EqualTo(64));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[1919], Is.EqualTo(0xCD));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var pixelData = new byte[1920];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PortfolioGraphicsFile { PixelData = pixelData };

    var bytes = PortfolioGraphicsWriter.ToBytes(original);
    var restored = PortfolioGraphicsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[1920];
    pixelData[0] = 0xFF;
    pixelData[100] = 0xAA;
    var original = new PortfolioGraphicsFile { PixelData = pixelData };

    var raw = PortfolioGraphicsFile.ToRawImage(original);
    var restored = PortfolioGraphicsFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[1920];
    pixelData[0] = 0xDE;
    var original = new PortfolioGraphicsFile { PixelData = pixelData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pgf");
    try {
      File.WriteAllBytes(tempPath, PortfolioGraphicsWriter.ToBytes(original));
      var restored = PortfolioGraphicsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

