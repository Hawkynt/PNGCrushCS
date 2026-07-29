using System;
using System.IO;
using FileFormat.Core;
using FileFormat.LastWordFont;

namespace FileFormat.LastWordFont.Tests;

[TestFixture]
public sealed class LastWordFontTests {

  private static LastWordFontFile _Sample() {
    var data = new byte[LastWordFontFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 41 % 256);

    return new() { GlyphData = data };
  }

  [Test]
  [Category("Unit")]
  public void Sheet_Is64GlyphsAt128x32() {
    Assert.Multiple(() => {
      Assert.That(LastWordFontFile.FileSize, Is.EqualTo(512));
      Assert.That(LastWordFontFile.SheetWidth, Is.EqualTo(128));
      Assert.That(LastWordFontFile.SheetHeight, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesGlyphBytes() {
    var original = _Sample();
    var restored = LastWordFontReader.FromBytes(LastWordFontWriter.ToBytes(original));

    Assert.That(restored.GlyphData, Is.EqualTo(original.GlyphData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => LastWordFontReader.FromBytes(new byte[256]));

  [Test]
  [Category("Unit")]
  public void RoundTrip_ThroughRawImage_PreservesEveryBit() {
    // One bit per pixel with no palette in between, so this has to be exact.
    var original = _Sample();
    var restored = LastWordFontFile.FromRawImage(LastWordFontFile.ToRawImage(original));

    Assert.That(restored.GlyphData, Is.EqualTo(original.GlyphData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_LaysGlyphsOut16PerRow() {
    var raw = LastWordFontFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(LastWordFontFile.SheetWidth));
      Assert.That(raw.Height, Is.EqualTo(LastWordFontFile.SheetHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(2));
    });
  }
}
