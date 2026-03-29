using System;
using FileFormat.Xcf;

namespace FileFormat.Xcf.Tests;

[TestFixture]
public sealed class XcfWriterTests {

  private static readonly byte[] _MAGIC = System.Text.Encoding.ASCII.GetBytes("gimp xcf ");

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new XcfFile {
      Width = 1,
      Height = 1,
      ColorMode = XcfColorMode.Rgb,
      PixelData = new byte[4]
    };

    var bytes = XcfWriter.ToBytes(file);

    for (var i = 0; i < _MAGIC.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(_MAGIC[i]), $"Magic byte {i} mismatch");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectVersion() {
    var file = new XcfFile {
      Width = 1,
      Height = 1,
      ColorMode = XcfColorMode.Rgb,
      PixelData = new byte[4]
    };

    var bytes = XcfWriter.ToBytes(file);

    // Version starts after "gimp xcf " at offset 9: "v001"
    Assert.That(bytes[9], Is.EqualTo((byte)'v'));
    Assert.That(bytes[10], Is.EqualTo((byte)'0'));
    Assert.That(bytes[11], Is.EqualTo((byte)'0'));
    Assert.That(bytes[12], Is.EqualTo((byte)'1'));
    Assert.That(bytes[13], Is.EqualTo(0)); // null terminator
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensions() {
    var file = new XcfFile {
      Width = 320,
      Height = 240,
      ColorMode = XcfColorMode.Rgb,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = XcfWriter.ToBytes(file);

    // After magic (14 bytes): width at offset 14, height at offset 18
    var width = (uint)(bytes[14] << 24 | bytes[15] << 16 | bytes[16] << 8 | bytes[17]);
    var height = (uint)(bytes[18] << 24 | bytes[19] << 16 | bytes[20] << 8 | bytes[21]);

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorModeCorrect() {
    var file = new XcfFile {
      Width = 1,
      Height = 1,
      ColorMode = XcfColorMode.Grayscale,
      PixelData = new byte[2]
    };

    var bytes = XcfWriter.ToBytes(file);

    // Color mode at offset 22
    var colorMode = (uint)(bytes[22] << 24 | bytes[23] << 16 | bytes[24] << 8 | bytes[25]);
    Assert.That(colorMode, Is.EqualTo(1)); // Grayscale = 1
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPresent_OutputNotEmpty() {
    var file = new XcfFile {
      Width = 2,
      Height = 2,
      ColorMode = XcfColorMode.Rgb,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = XcfWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(14)); // more than just the magic
  }
}
