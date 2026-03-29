using System;
using System.Text;
using FileFormat.Ics;

namespace FileFormat.Ics.Tests;

[TestFixture]
public sealed class IcsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithVersionHeader() {
    var file = new IcsFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.StartWith("ics_version\t2.0\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndMarker() {
    var file = new IcsFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[1]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("end\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsLayoutOrder() {
    var file = new IcsFile {
      Width = 4,
      Height = 3,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[12]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("layout\torder\tbits\tx\ty\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbContainsChannelDimension() {
    var file = new IcsFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      BitsPerSample = 8,
      PixelData = new byte[12]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("layout\torder\tbits\tx\ty\tch\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsLayoutSizes() {
    var file = new IcsFile {
      Width = 10,
      Height = 20,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[200]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("layout\tsizes\t8\t10\t20\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCompressionField() {
    var file = new IcsFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[1]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("representation\tcompression\tuncompressed\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GzipCompressionField() {
    var file = new IcsFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      BitsPerSample = 8,
      Compression = IcsCompression.Gzip,
      PixelData = new byte[1]
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("representation\tcompression\tgzip\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAfterEndMarker() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new IcsFile {
      Width = 3,
      Height = 1,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var endIndex = text.IndexOf("end\n", StringComparison.Ordinal);
    var dataStart = endIndex + "end\n".Length;

    Assert.That(bytes[dataStart], Is.EqualTo(0xAA));
    Assert.That(bytes[dataStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[dataStart + 2], Is.EqualTo(0xCC));
  }
}
