using System;
using FileFormat.UtahRle;

namespace FileFormat.UtahRle.Tests;

[TestFixture]
public sealed class UtahRleWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidGrayscale_StartsWithMagic() {
    var file = new UtahRleFile {
      Width = 2,
      Height = 2,
      NumChannels = 1,
      PixelData = new byte[2 * 2 * 1]
    };

    var bytes = UtahRleWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x52));
    Assert.That(bytes[1], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb_StartsWithMagic() {
    var file = new UtahRleFile {
      Width = 2,
      Height = 2,
      NumChannels = 3,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = UtahRleWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x52));
    Assert.That(bytes[1], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_WrittenCorrectly() {
    var file = new UtahRleFile {
      Width = 100,
      Height = 50,
      NumChannels = 3,
      PixelData = new byte[100 * 50 * 3]
    };

    var bytes = UtahRleWriter.ToBytes(file);

    var width = BitConverter.ToInt16(bytes, 6);
    var height = BitConverter.ToInt16(bytes, 8);
    Assert.That(width, Is.EqualTo(100));
    Assert.That(height, Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NumChannels_WrittenCorrectly() {
    var file = new UtahRleFile {
      Width = 1,
      Height = 1,
      NumChannels = 4,
      PixelData = new byte[1 * 1 * 4]
    };

    var bytes = UtahRleWriter.ToBytes(file);

    Assert.That(bytes[11], Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitsPerPixel_Always8() {
    var file = new UtahRleFile {
      Width = 1,
      Height = 1,
      NumChannels = 1,
      PixelData = new byte[1]
    };

    var bytes = UtahRleWriter.ToBytes(file);

    Assert.That(bytes[12], Is.EqualTo(8));
  }
}
