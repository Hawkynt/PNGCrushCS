using System;
using System.Text;
using FileFormat.Xbm;

namespace FileFormat.Xbm.Tests;

[TestFixture]
public sealed class XbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsWidthDefine() {
    var file = new XbmFile {
      Width = 16,
      Height = 8,
      Name = "test",
      PixelData = new byte[2 * 8]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("#define test_width 16"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHeightDefine() {
    var file = new XbmFile {
      Width = 16,
      Height = 8,
      Name = "test",
      PixelData = new byte[2 * 8]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("#define test_height 8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHexArray() {
    var file = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "img",
      PixelData = [0xFF]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("0xFF"));
    Assert.That(text, Does.Contain("static unsigned char img_bits[]"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithHotspot_ContainsHotspotDefines() {
    var file = new XbmFile {
      Width = 8,
      Height = 8,
      Name = "cursor",
      HotspotX = 4,
      HotspotY = 6,
      PixelData = new byte[8]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("#define cursor_x_hot 4"));
    Assert.That(text, Does.Contain("#define cursor_y_hot 6"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithoutHotspot_OmitsHotspotDefines() {
    var file = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "img",
      PixelData = [0x00]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Not.Contain("x_hot"));
    Assert.That(text, Does.Not.Contain("y_hot"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectFormat_EndsWithSemicolon() {
    var file = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "img",
      PixelData = [0xAA]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes).TrimEnd();

    Assert.That(text, Does.EndWith("};"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleBytes_SeparatedByCommas() {
    var file = new XbmFile {
      Width = 16,
      Height = 1,
      Name = "img",
      PixelData = [0xAA, 0xBB]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("0xAA, 0xBB"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UsesUppercaseHex() {
    var file = new XbmFile {
      Width = 8,
      Height = 1,
      Name = "img",
      PixelData = [0xAB]
    };

    var bytes = XbmWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("0xAB"));
    Assert.That(text, Does.Not.Contain("0xab"));
  }
}
