using System;
using FileFormat.Xpm;

namespace FileFormat.Xpm.Tests;

[TestFixture]
public sealed class XpmTextParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ValuesLine_ExtractsDimensions() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "4 3 2 1",
              ". c #000000",
              "# c #FFFFFF",
              "..##",
              "##..",
              ".#.#"
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.PaletteColorCount, Is.EqualTo(2));
    Assert.That(result.CharsPerPixel, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_CharsPerPixel1_MapsCorrectly() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "3 1 3 1",
              "A c #FF0000",
              "B c #00FF00",
              "C c #0000FF",
              "ABC"
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Parse_CharsPerPixel2_MapsCorrectly() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "2 1 2 2",
              "AA c #FF0000",
              "BB c #00FF00",
              "AABB"
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.CharsPerPixel, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_TransparentNone_SetsTransparentIndex() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "1 1 2 1",
              ". c None",
              "# c #FFFFFF",
              "."
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.TransparentIndex, Is.EqualTo(0));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HexColors_ParsedCorrectly() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "1 1 1 1",
              ". c #1A2B3C",
              "."
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.Palette[0], Is.EqualTo(0x1A), "R");
    Assert.That(result.Palette[1], Is.EqualTo(0x2B), "G");
    Assert.That(result.Palette[2], Is.EqualTo(0x3C), "B");
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsVariableName() {
    var xpm = """
              /* XPM */
              static char *my_image[] = {
              "1 1 1 1",
              ". c #000000",
              "."
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.Name, Is.EqualTo("my_image"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XpmTextParser.Parse(null!));
  }

  [Test]
  [Category("Unit")]
  public void Parse_NoMagic_ThrowsInvalidOperationException() {
    Assert.Throws<InvalidOperationException>(() => XpmTextParser.Parse("no magic here"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MultipleColors_AllParsed() {
    var xpm = """
              /* XPM */
              static char *img[] = {
              "2 2 4 1",
              ". c #000000",
              "# c #FF0000",
              "+ c #00FF00",
              "@ c #0000FF",
              ".#",
              "+@"
              };
              """;

    var result = XpmTextParser.Parse(xpm);

    Assert.That(result.PaletteColorCount, Is.EqualTo(4));
    Assert.That(result.Palette.Length, Is.EqualTo(12));
    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 0, 1, 2, 3 }));
  }
}
