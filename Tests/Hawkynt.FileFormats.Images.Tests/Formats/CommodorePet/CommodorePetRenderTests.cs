using System;
using FileFormat.Core;

namespace FileFormat.CommodorePet.Tests;

/// <summary>
/// Drawing a PETSCII screen rather than handing back the character codes.
/// </summary>
/// <remarks>
/// What came back before was the codes themselves, one to a pixel, as a picture 40 by 25 with a
/// 256-entry palette — so a screenful of art arrived as a thumbnail of meaningless indices, and it
/// counted as a successful decode. The codes are not the picture: each names a glyph in the
/// machine's character ROM, and the picture is those glyphs drawn eight pixels square in the colour
/// the file gives each cell.
/// <para/>
/// The colours are the last thousand bytes of the file rather than the thousand after the screen —
/// the sample carries twenty-four bytes between the two areas, so counting forward lands short and
/// paints the picture in the wrong colours entirely.
/// <para/>
/// Checked against RECOIL on a real file: all 64000 pixels come back identical.
/// </remarks>
[TestFixture]
public sealed class CommodorePetRenderTests {

  /// <summary>Builds a screen the way a saved one is laid out: a header, the codes, then the colours.</summary>
  private static byte[] _Screen(byte code, byte color, int gap = 0) {
    var data = new byte[2 + 1000 + gap + 1000];
    data[0] = 0x00;
    data[1] = 0x30;
    for (var i = 0; i < 1000; ++i) {
      data[2 + i] = code;
      data[2 + 1000 + gap + i] = color;
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void Decoded_IsTheScreenInPixelsAndNotTheCodes() {
    var image = CommodorePetFile.ToRawImage(CommodorePetReader.FromBytes(_Screen(1, 1)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320), "forty cells of eight pixels");
      Assert.That(image.Height, Is.EqualTo(200));
      Assert.That(image.PaletteCount, Is.EqualTo(16), "the machine has sixteen colours, not 256");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsNothingForASpace() {
    // Code 32 is a blank glyph, so every pixel stays the background whatever colour the cell names.
    var image = CommodorePetFile.ToRawImage(CommodorePetReader.FromBytes(_Screen(32, 7)));

    foreach (var index in image.PixelData)
      Assert.That(index, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsASolidGlyphInTheColourTheCellNames() {
    // Code 160 is the filled block, and its top bit inverts rather than choosing another glyph.
    var image = CommodorePetFile.ToRawImage(CommodorePetReader.FromBytes(_Screen(160, 5)));

    foreach (var index in image.PixelData)
      Assert.That(index, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheColoursFromTheEndAndNotFromJustAfterTheScreen() {
    // Twenty-four bytes sit between the two areas, as they do in a real file.
    var data = _Screen(160, 0, 24);
    for (var i = 0; i < 1000; ++i)
      data[2 + 1000 + 24 + i] = 4;

    var image = CommodorePetFile.ToRawImage(CommodorePetReader.FromBytes(data));

    foreach (var index in image.PixelData)
      Assert.That(index, Is.EqualTo(4), "counting forward from the screen would have read the gap");
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingTooSmallToHoldAScreen()
    => Assert.Throws<System.IO.InvalidDataException>(() => CommodorePetReader.FromBytes(new byte[64]));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenOfOneGlyphAndOneColour() {
    var original = CommodorePetReader.FromBytes(_Screen(160, 5));
    var restored = CommodorePetReader.FromBytes(CommodorePetWriter.ToBytes(CommodorePetFile.FromRawImage(CommodorePetFile.ToRawImage(original))));

    Assert.That(CommodorePetFile.ToRawImage(restored).PixelData, Is.EqualTo(CommodorePetFile.ToRawImage(original).PixelData));
  }
}
