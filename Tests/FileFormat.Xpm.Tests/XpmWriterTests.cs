using System;
using FileFormat.Xpm;

namespace FileFormat.Xpm.Tests;

[TestFixture]
public sealed class XpmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToText_ContainsMagicComment() {
    var file = _CreateMinimalFile();
    var text = XpmWriter.ToText(file);

    Assert.That(text, Does.Contain("/* XPM */"));
  }

  [Test]
  [Category("Unit")]
  public void ToText_HasCorrectValuesLine() {
    var file = _CreateMinimalFile();
    var text = XpmWriter.ToText(file);

    Assert.That(text, Does.Contain("\"2 2 2 1\""));
  }

  [Test]
  [Category("Unit")]
  public void ToText_HasColorDefinitions() {
    var file = new XpmFile {
      Width = 1,
      Height = 1,
      CharsPerPixel = 1,
      Name = "test",
      Palette = [0xFF, 0x00, 0x00],
      PaletteColorCount = 1,
      PixelData = [0]
    };

    var text = XpmWriter.ToText(file);

    Assert.That(text, Does.Contain("#FF0000"));
  }

  [Test]
  [Category("Unit")]
  public void ToText_HasTransparentNone() {
    var file = new XpmFile {
      Width = 1,
      Height = 1,
      CharsPerPixel = 1,
      Name = "test",
      Palette = [0x00, 0x00, 0x00],
      PaletteColorCount = 1,
      TransparentIndex = 0,
      PixelData = [0]
    };

    var text = XpmWriter.ToText(file);

    Assert.That(text, Does.Contain("None"));
  }

  [Test]
  [Category("Unit")]
  public void ToText_HasPixelRows() {
    var file = _CreateMinimalFile();
    var text = XpmWriter.ToText(file);

    // The text should contain pixel row strings with proper quotes
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var pixelRowCount = 0;
    var pastColors = false;
    foreach (var line in lines) {
      var trimmed = line.Trim();
      if (trimmed.StartsWith('"') && !trimmed.Contains(" c ") && !trimmed.Contains("\tc ") && pastColors)
        ++pixelRowCount;

      if (trimmed.Contains(" c ") || trimmed.Contains("\tc "))
        pastColors = true;
    }

    Assert.That(pixelRowCount, Is.EqualTo(2), "Should have 2 pixel rows");
  }

  [Test]
  [Category("Unit")]
  public void ToText_ContainsVariableName() {
    var file = new XpmFile {
      Width = 1,
      Height = 1,
      CharsPerPixel = 1,
      Name = "my_icon",
      Palette = [0xFF, 0xFF, 0xFF],
      PaletteColorCount = 1,
      PixelData = [0]
    };

    var text = XpmWriter.ToText(file);

    Assert.That(text, Does.Contain("my_icon"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReturnsUtf8() {
    var file = _CreateMinimalFile();
    var bytes = XpmWriter.ToBytes(file);
    var text = System.Text.Encoding.UTF8.GetString(bytes);

    Assert.That(text, Does.Contain("/* XPM */"));
  }

  private static XpmFile _CreateMinimalFile() => new() {
    Width = 2,
    Height = 2,
    CharsPerPixel = 1,
    Name = "test",
    Palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00],
    PaletteColorCount = 2,
    PixelData = [0, 1, 1, 0]
  };
}
