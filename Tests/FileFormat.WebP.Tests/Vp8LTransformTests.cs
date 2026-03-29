using System;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LTransformTests {

  #region SubtractGreen

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_AddsGreenToRedAndBlue() {
    var pixels = new uint[] { 0xFF004020u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    var g = (byte)((0xFF004020u >> 8) & 0xFF);
    var expectedR = (byte)(((0xFF004020u >> 16) & 0xFF) + g);
    var expectedB = (byte)((0xFF004020u & 0xFF) + g);
    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo(expectedR));
    Assert.That((byte)(pixels[0] & 0xFF), Is.EqualTo(expectedB));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_PreservesAlphaAndGreen() {
    var pixels = new uint[] { 0xAB00CD00u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    Assert.That((byte)((pixels[0] >> 24) & 0xFF), Is.EqualTo(0xAB));
    Assert.That((byte)((pixels[0] >> 8) & 0xFF), Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_ZeroGreen_NoChange() {
    var original = 0xFF800060u;
    var pixels = new uint[] { original };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    Assert.That(pixels[0], Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_WrapsAround() {
    var pixels = new uint[] { 0xFFFEFF01u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    var g = 0xFF;
    var r = (0xFE + g) & 0xFF;
    var b = (0x01 + g) & 0xFF;
    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo((byte)r));
    Assert.That((byte)(pixels[0] & 0xFF), Is.EqualTo((byte)b));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_MultiplePixels() {
    var pixels = new uint[] { 0xFF001000u, 0xFF002000u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 2, 1);

    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo(0x10));
    Assert.That((byte)((pixels[1] >> 16) & 0xFF), Is.EqualTo(0x20));
  }

  #endregion

  #region ColorIndexing

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_SmallPalette_PacksPixels() {
    var palette = new uint[] { 0xFF000000u, 0xFFFFFFFFu };
    var ci = new Vp8LColorIndexingTransform(palette, 8);

    Assert.That(ci.EncodedWidth, Is.EqualTo(1));
    Assert.That(ci.OriginalWidth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_4ColorPalette() {
    var palette = new uint[] { 0xFF000000u, 0xFF333333u, 0xFF666666u, 0xFF999999u };
    var ci = new Vp8LColorIndexingTransform(palette, 16);

    Assert.That(ci.EncodedWidth, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_LargePalette_NoPacking() {
    var palette = new uint[256];
    for (var i = 0; i < 256; ++i)
      palette[i] = 0xFF000000u | ((uint)i << 16) | ((uint)i << 8) | (uint)i;
    var ci = new Vp8LColorIndexingTransform(palette, 100);

    Assert.That(ci.EncodedWidth, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_InverseTransform_LookupsByGreenChannel() {
    // Use a palette > 16 entries so bitsPerPixel=8 and the simple lookup branch is taken
    var palette = new uint[17];
    palette[0] = 0xFFFF0000u;
    palette[1] = 0xFF00FF00u;
    palette[2] = 0xFF0000FFu;
    for (var i = 3; i < 17; ++i)
      palette[i] = 0xFF000000u;

    // Green channel holds the palette index
    var pixels = new uint[3];
    pixels[0] = 0x00000000u; // green=0 -> palette[0]=red
    pixels[1] = 0x00000100u; // green=1 -> palette[1]=green
    pixels[2] = 0x00000200u; // green=2 -> palette[2]=blue

    var ci = new Vp8LColorIndexingTransform(palette, 3);
    ci.InverseTransform(pixels, 3, 1);

    Assert.That(pixels[0], Is.EqualTo(0xFFFF0000u));
    Assert.That(pixels[1], Is.EqualTo(0xFF00FF00u));
    Assert.That(pixels[2], Is.EqualTo(0xFF0000FFu));
  }

  #endregion
}
