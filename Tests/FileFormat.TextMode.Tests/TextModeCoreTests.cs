using FileFormat.TextMode;

namespace FileFormat.TextMode.Tests;

[TestFixture]
public sealed class TextCellTests {

  [Test]
  [Category("Unit")]
  public void AttributeByte_PacksFgBgBlink() {
    var c = new TextCell(0x41, Foreground: 0x0F, Background: 0x07, Blink: true);
    Assert.That(c.AttributeByte, Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromAttribute_RoundTrips_WithBlink() {
    var c = TextCell.FromAttribute(0x41, 0xFF, useBlinkBit: true);
    Assert.That(c.CodePoint, Is.EqualTo(0x41));
    Assert.That(c.Foreground, Is.EqualTo(0x0F));
    Assert.That(c.Background, Is.EqualTo(0x07));
    Assert.That(c.Blink, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromAttribute_RoundTrips_NonBlink_BgUses16() {
    var c = TextCell.FromAttribute(0x41, 0xFF, useBlinkBit: false);
    Assert.That(c.Background, Is.EqualTo(0x0F));
    Assert.That(c.Blink, Is.False);
  }
}

[TestFixture]
public sealed class Cp437Tests {

  [Test]
  [Category("Unit")]
  public void ToUnicode_HasAllAsciiPrintables() {
    for (var i = 0x20; i <= 0x7E; ++i)
      Assert.That((int)Cp437.ToUnicode[i], Is.EqualTo(i));
  }

  [Test]
  [Category("Unit")]
  public void ToUnicode_HasBoxDrawing() {
    Assert.That(Cp437.ToUnicode[0xC9], Is.EqualTo('╔'));
    Assert.That(Cp437.ToUnicode[0xBB], Is.EqualTo('╗'));
    Assert.That(Cp437.ToUnicode[0xC8], Is.EqualTo('╚'));
    Assert.That(Cp437.ToUnicode[0xBC], Is.EqualTo('╝'));
  }

  [Test]
  [Category("Unit")]
  public void FromUnicode_AsciiRoundTrips() {
    Assert.That(Cp437.FromUnicode('A'), Is.EqualTo(0x41));
    Assert.That(Cp437.FromUnicode('0'), Is.EqualTo(0x30));
    Assert.That(Cp437.FromUnicode(' '), Is.EqualTo(0x20));
  }

  [Test]
  [Category("Unit")]
  public void FromUnicode_BoxDrawingRoundTrips() {
    Assert.That(Cp437.FromUnicode('╔'), Is.EqualTo(0xC9));
    Assert.That(Cp437.FromUnicode('═'), Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void GetString_DecodesBoxDrawingArt() {
    byte[] bytes = [0xC9, 0xCD, 0xCD, 0xBB];
    Assert.That(Cp437.GetString(bytes), Is.EqualTo("╔══╗"));
  }
}

[TestFixture]
public sealed class BitmapFontTests {

  [Test]
  [Category("Unit")]
  public void DefaultVga8x16_HasExpectedDimensions() {
    var f = BitmapFont.DefaultVga8x16;
    Assert.That(f.CellWidth, Is.EqualTo(8));
    Assert.That(f.CellHeight, Is.EqualTo(16));
    Assert.That(f.GlyphData.Length, Is.EqualTo(4096));
  }

  [Test]
  [Category("Unit")]
  public void DefaultVga_FullBlock_IsAllPixelsLit() {
    var f = BitmapFont.DefaultVga8x16;
    for (var r = 0; r < 16; ++r)
      Assert.That(f.GetGlyphRow(0xDB, r), Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void DefaultVga_LowerHalfBlock_TopHalfClear_BottomHalfLit() {
    var f = BitmapFont.DefaultVga8x16;
    for (var r = 0; r < 8; ++r)  Assert.That(f.GetGlyphRow(0xDC, r), Is.EqualTo(0x00));
    for (var r = 8; r < 16; ++r) Assert.That(f.GetGlyphRow(0xDC, r), Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsWrongLength() {
    Assert.Throws<System.ArgumentException>(() => BitmapFont.FromBytes(8, 16, new byte[100]));
  }
}

[TestFixture]
public sealed class TextScreenRenderTests {

  [Test]
  [Category("Unit")]
  public void Render_SolidBlock_ProducesAllForegroundPixels() {
    var screen = new TextScreen {
      ColumnCount = 1,
      RowCount = 1,
      Cells = [new TextCell(0xDB /* full block */, Foreground: 14 /* yellow */, Background: 4 /* red */)],
    };
    var img = TextScreenRenderer.Render(screen);
    Assert.That(img.Width, Is.EqualTo(8));
    Assert.That(img.Height, Is.EqualTo(16));
    for (var i = 0; i < 128; ++i) {
      Assert.That(img.PixelData[i * 3],     Is.EqualTo(0xFF));
      Assert.That(img.PixelData[i * 3 + 1], Is.EqualTo(0xFF));
      Assert.That(img.PixelData[i * 3 + 2], Is.EqualTo(0x55));
    }
  }

  [Test]
  [Category("Unit")]
  public void Render_Space_AllBackgroundPixels() {
    var screen = new TextScreen {
      ColumnCount = 1,
      RowCount = 1,
      Cells = [new TextCell(0x20, Foreground: 15, Background: 1 /* blue */)],
    };
    var img = TextScreenRenderer.Render(screen);
    Assert.That(img.PixelData[0], Is.EqualTo(0x00));
    Assert.That(img.PixelData[1], Is.EqualTo(0x00));
    Assert.That(img.PixelData[2], Is.EqualTo(0xAA));
  }
}
