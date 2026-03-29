using System;
using System.Text;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class HdrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithRadianceMagic() {
    var file = new HdrFile {
      Width = 2,
      Height = 2,
      PixelData = new float[2 * 2 * 3]
    };

    var bytes = HdrWriter.ToBytes(file);
    var header = Encoding.ASCII.GetString(bytes, 0, 10);

    Assert.That(header, Is.EqualTo("#?RADIANCE"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFormatLine() {
    var file = new HdrFile {
      Width = 2,
      Height = 2,
      PixelData = new float[2 * 2 * 3]
    };

    var bytes = HdrWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("FORMAT=32-bit_rle_rgbe"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsResolutionString() {
    var file = new HdrFile {
      Width = 4,
      Height = 3,
      PixelData = new float[4 * 3 * 3]
    };

    var bytes = HdrWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("-Y 3 +X 4"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithExposure_ContainsExposureLine() {
    var file = new HdrFile {
      Width = 2,
      Height = 2,
      Exposure = 2.5f,
      PixelData = new float[2 * 2 * 3]
    };

    var bytes = HdrWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("EXPOSURE=2.5"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DefaultExposure_NoExposureLine() {
    var file = new HdrFile {
      Width = 2,
      Height = 2,
      PixelData = new float[2 * 2 * 3]
    };

    var bytes = HdrWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Not.Contain("EXPOSURE="));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HdrWriter.ToBytes(null!));
  }
}
