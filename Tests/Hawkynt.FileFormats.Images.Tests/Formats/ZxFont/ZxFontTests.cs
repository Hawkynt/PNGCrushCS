using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxFont;

namespace FileFormat.ZxFont.Tests;

[TestFixture]
public sealed class ZxFontTests {

  private static ZxFontFile _Sample() {
    var data = new byte[ZxFontFile.FullSetSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 37 % 256);

    return new() { GlyphData = data };
  }

  [Test]
  [Category("Unit")]
  public void FullSet_Is2048BytesAnd64Rows() {
    Assert.Multiple(() => {
      Assert.That(ZxFontFile.FullSetSize, Is.EqualTo(2048));
      Assert.That(ZxFontFile.FullSetHeight, Is.EqualTo(64));
      Assert.That(ZxFontFile.SheetWidth, Is.EqualTo(256));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeightFor_RoundsUpToWholeGlyphRows() {
    Assert.Multiple(() => {
      Assert.That(ZxFontFile.HeightFor(256), Is.EqualTo(8), "one full glyph row");
      Assert.That(ZxFontFile.HeightFor(8), Is.EqualTo(8), "a single glyph still occupies a row");
      Assert.That(ZxFontFile.HeightFor(2048), Is.EqualTo(64));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesGlyphBytes() {
    var original = _Sample();
    var restored = ZxFontReader.FromBytes(ZxFontWriter.ToBytes(original));

    Assert.That(restored.GlyphData, Is.EqualTo(original.GlyphData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAPartialGlyph()
    => Assert.Throws<InvalidDataException>(() => ZxFontReader.FromBytes(new byte[12]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_LaysGlyphsOut32PerRow() {
    var raw = ZxFontFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(ZxFontFile.SheetWidth));
      Assert.That(raw.Height, Is.EqualTo(ZxFontFile.FullSetHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_ThroughRawImage_PreservesEveryBit() {
    var original = _Sample();
    var restored = ZxFontFile.FromRawImage(ZxFontFile.ToRawImage(original));

    // One bit per pixel with no palette in between, so this has to be exact.
    Assert.That(restored.GlyphData, Is.EqualTo(original.GlyphData));
  }
}
