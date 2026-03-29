using System;
using System.Text;
using FileFormat.Phm;

namespace FileFormat.Phm.Tests;

[TestFixture]
public sealed class PhmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_StartsWithPhMagic() {
    var file = new PhmFile {
      Width = 2, Height = 2, ColorMode = PhmColorMode.Rgb,
      Scale = 1.0f, IsLittleEndian = true, PixelData = new Half[12]
    };

    var bytes = PhmWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'P'));
      Assert.That(bytes[1], Is.EqualTo((byte)'H'));
      Assert.That(bytes[2], Is.EqualTo((byte)'\n'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Grayscale_StartsWithPhMagic() {
    var file = new PhmFile {
      Width = 2, Height = 2, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true, PixelData = new Half[4]
    };

    var bytes = PhmWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'P'));
      Assert.That(bytes[1], Is.EqualTo((byte)'h'));
      Assert.That(bytes[2], Is.EqualTo((byte)'\n'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasCorrectDimensions() {
    var file = new PhmFile {
      Width = 5, Height = 3, ColorMode = PhmColorMode.Rgb,
      Scale = 1.0f, IsLittleEndian = true, PixelData = new Half[45]
    };

    var bytes = PhmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(lines[1], Is.EqualTo("5 3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LittleEndian_NegativeScale() {
    var file = new PhmFile {
      Width = 1, Height = 1, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true, PixelData = new Half[1]
    };

    var bytes = PhmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(float.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture), Is.LessThan(0f));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BigEndian_PositiveScale() {
    var file = new PhmFile {
      Width = 1, Height = 1, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = false, PixelData = new Half[1]
    };

    var bytes = PhmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(float.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0f));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HalfDataPresent() {
    var pixelData = new Half[] { (Half)1.0f, (Half)0.5f, (Half)0.25f };
    var file = new PhmFile {
      Width = 1, Height = 1, ColorMode = PhmColorMode.Rgb,
      Scale = 1.0f, IsLittleEndian = true, PixelData = pixelData
    };

    var bytes = PhmWriter.ToBytes(file);
    var headerEnd = 0;
    var newlineCount = 0;
    for (var i = 0; i < bytes.Length && newlineCount < 3; ++i)
      if (bytes[i] == (byte)'\n') {
        ++newlineCount;
        headerEnd = i + 1;
      }

    Assert.That(bytes.Length - headerEnd, Is.EqualTo(3 * 2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GrayscaleOutputSize() {
    var file = new PhmFile {
      Width = 4, Height = 3, ColorMode = PhmColorMode.Grayscale,
      Scale = 1.0f, IsLittleEndian = true, PixelData = new Half[12]
    };

    var bytes = PhmWriter.ToBytes(file);
    var headerEnd = 0;
    var newlineCount = 0;
    for (var i = 0; i < bytes.Length && newlineCount < 3; ++i)
      if (bytes[i] == (byte)'\n') {
        ++newlineCount;
        headerEnd = i + 1;
      }

    Assert.That(bytes.Length - headerEnd, Is.EqualTo(4 * 3 * 2));
  }
}
