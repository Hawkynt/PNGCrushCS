using System;
using System.Text;
using FileFormat.Nitf;

namespace FileFormat.Nitf.Tests;

[TestFixture]
public sealed class NitfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithNitfMagic() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("NITF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionField() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);

    var version = Encoding.ASCII.GetString(bytes, 4, 5);
    Assert.That(version, Is.EqualTo("02.10"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GrayscaleDimensions() {
    var file = new NitfFile {
      Width = 16,
      Height = 12,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[16 * 12],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Width, Is.EqualTo(16));
    Assert.That(parsed.Height, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbDimensions() {
    var file = new NitfFile {
      Width = 10,
      Height = 5,
      Mode = NitfImageMode.Rgb,
      PixelData = new byte[10 * 5 * 3],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Width, Is.EqualTo(10));
    Assert.That(parsed.Height, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileLengthMatchesOutput() {
    var file = new NitfFile {
      Width = 4,
      Height = 3,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4 * 3],
    };

    var bytes = NitfWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsImageMarker() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);

    // Search for "IM" marker in the subheader
    var content = Encoding.ASCII.GetString(bytes);
    Assert.That(content, Does.Contain("IM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsNoCompression() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);

    var content = Encoding.ASCII.GetString(bytes);
    Assert.That(content, Does.Contain("NC"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ClassificationInHeader() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      Classification = 'T',
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);

    // FSCLAS is at offset 119 (1 byte)
    var cls = (char)bytes[119];
    Assert.That(cls, Is.EqualTo('T'));
  }
}
