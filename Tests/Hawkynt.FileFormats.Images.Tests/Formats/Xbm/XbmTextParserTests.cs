using System;
using System.IO;
using FileFormat.Xbm;

namespace FileFormat.Xbm.Tests;

[TestFixture]
public sealed class XbmTextParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XbmTextParser.Parse(null!));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MissingDefines_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => XbmTextParser.Parse("no defines here"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsWidthAndHeight() {
    var text =
      "#define icon_width 16\n" +
      "#define icon_height 8\n" +
      "static unsigned char icon_bits[] = {\n" +
      string.Join(", ", _GenerateHexBytes(16)) +
      "\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsName() {
    var text =
      "#define myimage_width 8\n" +
      "#define myimage_height 1\n" +
      "static unsigned char myimage_bits[] = {\n" +
      "   0xFF\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.Name, Is.EqualTo("myimage"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithHotspot_ExtractsHotspotCoordinates() {
    var text =
      "#define ptr_width 8\n" +
      "#define ptr_height 8\n" +
      "#define ptr_x_hot 1\n" +
      "#define ptr_y_hot 2\n" +
      "static unsigned char ptr_bits[] = {\n" +
      string.Join(", ", _GenerateHexBytes(8)) +
      "\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.HotspotX, Is.EqualTo(1));
    Assert.That(result.HotspotY, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithoutHotspot_HotspotIsNull() {
    var text =
      "#define img_width 8\n" +
      "#define img_height 1\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0x00\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.HotspotX, Is.Null);
    Assert.That(result.HotspotY, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void Parse_HexValues_ExtractedCorrectly() {
    var text =
      "#define img_width 8\n" +
      "#define img_height 2\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0xAA, 0x55\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0x55));
  }

  [Test]
  [Category("Unit")]
  public void Parse_LowercaseHex_ParsedCorrectly() {
    var text =
      "#define img_width 8\n" +
      "#define img_height 1\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0xab\n};\n";

    var result = XbmTextParser.Parse(text);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void Parse_InsufficientData_ThrowsInvalidDataException() {
    var text =
      "#define img_width 16\n" +
      "#define img_height 2\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0xFF\n};\n"; // only 1 byte, need 4

    Assert.Throws<InvalidDataException>(() => XbmTextParser.Parse(text));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WidthNotMultipleOf8_CorrectBytesPerRow() {
    var text =
      "#define img_width 10\n" +
      "#define img_height 1\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0xFF, 0x03\n};\n"; // ceil(10/8) = 2 bytes per row

    var result = XbmTextParser.Parse(text);

    Assert.That(result.PixelData, Has.Length.EqualTo(2));
    Assert.That(result.Width, Is.EqualTo(10));
  }

  private static string[] _GenerateHexBytes(int count) {
    var result = new string[count];
    for (var i = 0; i < count; ++i)
      result[i] = $"0x{(byte)(i * 17 % 256):X2}";
    return result;
  }
}
