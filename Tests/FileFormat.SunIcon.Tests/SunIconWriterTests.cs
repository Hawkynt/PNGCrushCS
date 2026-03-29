using System;
using System.Text;
using FileFormat.SunIcon;

namespace FileFormat.SunIcon.Tests;

[TestFixture]
public sealed class SunIconWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunIconWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCommentHeader() {
    var file = new SunIconFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[32]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.StartWith("/* "));
    Assert.That(text, Does.Contain("*/"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsWidthInHeader() {
    var file = new SunIconFile {
      Width = 64,
      Height = 32,
      PixelData = new byte[256]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("Width=64"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHeightInHeader() {
    var file = new SunIconFile {
      Width = 64,
      Height = 32,
      PixelData = new byte[256]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("Height=32"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDepth1() {
    var file = new SunIconFile {
      Width = 16,
      Height = 1,
      PixelData = new byte[2]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("Depth=1"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsValidBitsPerItem() {
    var file = new SunIconFile {
      Width = 16,
      Height = 1,
      PixelData = new byte[2]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("Valid_bits_per_item=16"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHexValues() {
    var file = new SunIconFile {
      Width = 16,
      Height = 1,
      PixelData = [0xFF, 0xAA]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("0xFFAA"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleItems_SeparatedByCommas() {
    var file = new SunIconFile {
      Width = 32,
      Height = 1,
      PixelData = [0xFF, 0x00, 0xAA, 0x55]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("0xFF00,0xAA55"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFormatVersion() {
    var file = new SunIconFile {
      Width = 16,
      Height = 1,
      PixelData = new byte[2]
    };

    var bytes = SunIconWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("Format_version=1"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsAscii() {
    var file = new SunIconFile {
      Width = 16,
      Height = 1,
      PixelData = new byte[2]
    };

    var bytes = SunIconWriter.ToBytes(file);

    foreach (var b in bytes)
      Assert.That(b, Is.LessThan(128), "Output should be pure ASCII.");
  }
}
