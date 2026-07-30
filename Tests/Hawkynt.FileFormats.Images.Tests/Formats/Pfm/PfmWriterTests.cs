using System;
using System.Text;
using FileFormat.Pfm;

namespace FileFormat.Pfm.Tests;

[TestFixture]
public sealed class PfmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_StartsWithPfMagic() {
    var file = new PfmFile {
      Width = 2,
      Height = 2,
      ColorMode = PfmColorMode.Rgb,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = new float[2 * 2 * 3]
    };

    var bytes = PfmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'F'));
    Assert.That(bytes[2], Is.EqualTo((byte)'\n'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Grayscale_StartsWithPfMagic() {
    var file = new PfmFile {
      Width = 2,
      Height = 2,
      ColorMode = PfmColorMode.Grayscale,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = new float[2 * 2]
    };

    var bytes = PfmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'f'));
    Assert.That(bytes[2], Is.EqualTo((byte)'\n'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasCorrectDimensions() {
    var file = new PfmFile {
      Width = 5,
      Height = 3,
      ColorMode = PfmColorMode.Rgb,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = new float[5 * 3 * 3]
    };

    var bytes = PfmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(lines[1], Is.EqualTo("5 3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LittleEndian_NegativeScale() {
    var file = new PfmFile {
      Width = 1,
      Height = 1,
      ColorMode = PfmColorMode.Grayscale,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = new float[1]
    };

    var bytes = PfmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(float.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture), Is.LessThan(0f));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BigEndian_PositiveScale() {
    var file = new PfmFile {
      Width = 1,
      Height = 1,
      ColorMode = PfmColorMode.Grayscale,
      Scale = 1.0f,
      IsLittleEndian = false,
      PixelData = new float[1]
    };

    var bytes = PfmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var lines = text.Split('\n');

    Assert.That(float.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0f));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FloatDataPresent() {
    var pixelData = new float[] { 1.0f, 0.5f, 0.25f };
    var file = new PfmFile {
      Width = 1,
      Height = 1,
      ColorMode = PfmColorMode.Rgb,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = pixelData
    };

    var bytes = PfmWriter.ToBytes(file);

    // Header ends after third '\n', then 12 bytes of float data (3 floats x 4 bytes)
    var headerEnd = 0;
    var newlineCount = 0;
    for (var i = 0; i < bytes.Length && newlineCount < 3; ++i)
      if (bytes[i] == (byte)'\n') {
        ++newlineCount;
        headerEnd = i + 1;
      }

    Assert.That(bytes.Length - headerEnd, Is.EqualTo(3 * 4));
  }
}
