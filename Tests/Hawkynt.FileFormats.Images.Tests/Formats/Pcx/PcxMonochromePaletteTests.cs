using System;
using FileFormat.Core;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

/// <summary>
/// The two colours a monochrome PCX names in its header.
/// </summary>
/// <remarks>
/// They were ignored and a fixed black-and-white pair used instead. The pair happens to be right for
/// most files, but a PCX may name any two colours — amber on black was a common enough choice — and
/// a file naming them was drawn in the wrong ones.
/// <para/>
/// Where the header names nothing, which is what leaving both entries black amounts to, the fixed
/// pair still stands in. Two real samples exercise both cases: a PCX whose header is empty and a DCX
/// whose header states black and white, and both come out as they did before this change.
/// <para/>
/// Worth recording: ImageMagick draws both of those samples inverted — a white trumpeter on black,
/// a fax cover sheet in negative — and the DCX states black and white in its own header, so the
/// disagreement is that tool's and not this one's.
/// </remarks>
[TestFixture]
public sealed class PcxMonochromePaletteTests {

  private static PcxFile _Mono(byte[]? palette) => new() {
    Width = 8,
    Height = 1,
    BitsPerPixel = 1,
    ColorMode = PcxColorMode.Monochrome,
    PixelData = [0b10101010],
    Palette = palette,
    PaletteColorCount = palette == null ? 0 : 2,
  };

  [Test]
  [Category("Unit")]
  public void TheColoursTheHeaderNamesAreUsed() {
    // Amber on black, which a fixed black-and-white pair would throw away.
    var image = PcxFile.ToRawImage(_Mono([0, 0, 0, 255, 176, 0]));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![3], Is.EqualTo(255));
      Assert.That(image.Palette![4], Is.EqualTo(176));
      Assert.That(image.Palette![5], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyHeaderFallsBackToPaperAndInk() {
    // Both entries black is an unfilled header, not a choice of colours.
    var image = PcxFile.ToRawImage(_Mono([0, 0, 0, 0, 0, 0]));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.Zero);
      Assert.That(image.Palette![3], Is.EqualTo(255));
      Assert.That(image.Palette![4], Is.EqualTo(255));
      Assert.That(image.Palette![5], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void NoHeaderPaletteAtAllFallsBackTheSameWay() {
    var image = PcxFile.ToRawImage(_Mono(null));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.That(image.Palette![3], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void TheBitsThemselvesAreUntouchedByTheChoice() {
    var stated = PcxFile.ToRawImage(_Mono([0, 0, 0, 255, 176, 0]));
    var fallback = PcxFile.ToRawImage(_Mono(null));

    Assert.That(stated.PixelData, Is.EqualTo(fallback.PixelData));
  }
}
