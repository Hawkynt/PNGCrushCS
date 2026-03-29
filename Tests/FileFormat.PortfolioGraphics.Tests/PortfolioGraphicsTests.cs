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
public sealed class PortfolioGraphicsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PortfolioGraphicsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectSize() {
    var file = new PortfolioGraphicsFile { PixelData = new byte[1920] };

    var bytes = PortfolioGraphicsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(3848));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasZeroHeader() {
    var file = new PortfolioGraphicsFile { PixelData = new byte[1920] };

    var bytes = PortfolioGraphicsWriter.ToBytes(file);

    for (var i = 0; i < 8; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Header byte {i} should be zero");
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

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_DefaultWidth_Is240() {
    Assert.That(new PortfolioGraphicsFile().Width, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_DefaultHeight_Is64() {
    Assert.That(new PortfolioGraphicsFile().Height, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PortfolioGraphicsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PortfolioGraphicsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_ToRawImage_ReturnsIndexed1() {
    var file = new PortfolioGraphicsFile { PixelData = new byte[1920] };
    var raw = PortfolioGraphicsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void PortfolioGraphicsFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 240, Height = 64,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[240 * 64 * 3],
    };
    Assert.Throws<ArgumentException>(() => PortfolioGraphicsFile.FromRawImage(raw));
  }
}
