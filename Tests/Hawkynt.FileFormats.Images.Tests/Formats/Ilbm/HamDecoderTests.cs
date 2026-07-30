using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class HamDecoderTests {

  [Test]
  [Category("Unit")]
  public void Decode_Ham6_PaletteSet_UsesColor() {
    var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 }; // R, G, B
    var indexedData = new byte[] { 0, 1, 2 }; // palette indices 0, 1, 2

    var rgb = HamDecoder.Decode(indexedData, palette, 3, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255)); // pixel 0 R
      Assert.That(rgb[1], Is.EqualTo(0));   // pixel 0 G
      Assert.That(rgb[2], Is.EqualTo(0));   // pixel 0 B
      Assert.That(rgb[3], Is.EqualTo(0));   // pixel 1 R
      Assert.That(rgb[4], Is.EqualTo(255)); // pixel 1 G
      Assert.That(rgb[5], Is.EqualTo(0));   // pixel 1 B
      Assert.That(rgb[6], Is.EqualTo(0));   // pixel 2 R
      Assert.That(rgb[7], Is.EqualTo(0));   // pixel 2 G
      Assert.That(rgb[8], Is.EqualTo(255)); // pixel 2 B
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_Ham6_ModifyBlue_ChangesOnlyBlue() {
    var palette = new byte[] { 100, 150, 200 }; // single palette entry
    // pixel 0 = palette 0 (control=00), pixel 1 = modify blue with value 15 (control=01, value=0x0F = 0x1F)
    var indexedData = new byte[] { 0, (1 << 4) | 0x0F };

    var rgb = HamDecoder.Decode(indexedData, palette, 2, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(100)); // pixel 0 R (from palette)
      Assert.That(rgb[1], Is.EqualTo(150)); // pixel 0 G
      Assert.That(rgb[2], Is.EqualTo(200)); // pixel 0 B
      Assert.That(rgb[3], Is.EqualTo(100)); // pixel 1 R (held)
      Assert.That(rgb[4], Is.EqualTo(150)); // pixel 1 G (held)
      Assert.That(rgb[5], Is.Not.EqualTo(200)); // pixel 1 B (modified)
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_Ham6_ModifyRed_ChangesOnlyRed() {
    var palette = new byte[] { 100, 150, 200 };
    // pixel 0 = palette 0, pixel 1 = modify red with value 8 (control=10, value=8 = 0x28)
    var indexedData = new byte[] { 0, (2 << 4) | 8 };

    var rgb = HamDecoder.Decode(indexedData, palette, 2, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[3], Is.Not.EqualTo(100)); // pixel 1 R (modified)
      Assert.That(rgb[4], Is.EqualTo(150)); // pixel 1 G (held)
      Assert.That(rgb[5], Is.EqualTo(200)); // pixel 1 B (held)
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_Ham6_ModifyGreen_ChangesOnlyGreen() {
    var palette = new byte[] { 100, 150, 200 };
    // pixel 0 = palette 0, pixel 1 = modify green with value 5 (control=11, value=5 = 0x35)
    var indexedData = new byte[] { 0, (3 << 4) | 5 };

    var rgb = HamDecoder.Decode(indexedData, palette, 2, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[3], Is.EqualTo(100)); // pixel 1 R (held)
      Assert.That(rgb[4], Is.Not.EqualTo(150)); // pixel 1 G (modified)
      Assert.That(rgb[5], Is.EqualTo(200)); // pixel 1 B (held)
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_Ham8_PaletteSet_UsesColor() {
    var palette = new byte[256 * 3];
    palette[0] = 128; palette[1] = 64; palette[2] = 32;

    var indexedData = new byte[] { 0 }; // palette index 0

    var rgb = HamDecoder.Decode(indexedData, palette, 1, 1, 8);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(128));
      Assert.That(rgb[1], Is.EqualTo(64));
      Assert.That(rgb[2], Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_AllPalette_NoModify() {
    var palette = new byte[] { 10, 20, 30, 40, 50, 60 };
    var indexedData = new byte[] { 0, 1, 0 };

    var rgb = HamDecoder.Decode(indexedData, palette, 3, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(10));
      Assert.That(rgb[1], Is.EqualTo(20));
      Assert.That(rgb[2], Is.EqualTo(30));
      Assert.That(rgb[3], Is.EqualTo(40));
      Assert.That(rgb[4], Is.EqualTo(50));
      Assert.That(rgb[5], Is.EqualTo(60));
      Assert.That(rgb[6], Is.EqualTo(10));
      Assert.That(rgb[7], Is.EqualTo(20));
      Assert.That(rgb[8], Is.EqualTo(30));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_NewRowResetsHeldColor() {
    var palette = new byte[] { 100, 150, 200 };
    // Row 0: palette 0, then modify blue
    // Row 1: palette 0 (should reset to palette, not carry from previous row)
    var indexedData = new byte[] { 0, (1 << 4) | 0x0F, 0, 0 };

    var rgb = HamDecoder.Decode(indexedData, palette, 2, 2, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[6], Is.EqualTo(100)); // row 1, pixel 0 R
      Assert.That(rgb[7], Is.EqualTo(150)); // row 1, pixel 0 G
      Assert.That(rgb[8], Is.EqualTo(200)); // row 1, pixel 0 B
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_NullIndexedData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HamDecoder.Decode(null!, new byte[3], 1, 1, 6));
  }

  [Test]
  [Category("Unit")]
  public void Decode_NullPalette_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HamDecoder.Decode(new byte[1], null!, 1, 1, 6));
  }
}
