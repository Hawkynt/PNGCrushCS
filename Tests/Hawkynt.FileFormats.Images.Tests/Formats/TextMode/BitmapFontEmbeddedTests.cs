using FileFormat.TextMode;

namespace FileFormat.TextMode.Tests;

[TestFixture]
public sealed class BitmapFontEmbeddedTests {

  [Test]
  [Category("Unit")]
  public void IbmVga8x16_LazyLoads_FromEmbeddedDeflate() {
    var f = BitmapFontEmbedded.IbmVga8x16;
    Assert.That(f.CellWidth, Is.EqualTo(8));
    Assert.That(f.CellHeight, Is.EqualTo(16));
    Assert.That(f.GlyphData.Length, Is.EqualTo(4096));
    // Full block (0xDB) should be all-pixels-lit in every embedded font.
    for (var r = 0; r < 16; ++r)
      Assert.That(f.GetGlyphRow(0xDB, r), Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void IbmEga8x14_LazyLoads_With14RowsPerGlyph() {
    var f = BitmapFontEmbedded.IbmEga8x14;
    Assert.That(f.CellHeight, Is.EqualTo(14));
    Assert.That(f.GlyphData.Length, Is.EqualTo(256 * 14));
  }

  [Test]
  [Category("Unit")]
  public void IbmCga8x8_LazyLoads_With8RowsPerGlyph() {
    var f = BitmapFontEmbedded.IbmCga8x8;
    Assert.That(f.CellHeight, Is.EqualTo(8));
    Assert.That(f.GlyphData.Length, Is.EqualTo(256 * 8));
  }

  [Test]
  [Category("Unit")]
  public void AllEmbeddedFonts_AreLoadable() {
    foreach (var (label, get, cellW, cellH) in BitmapFontEmbedded.All) {
      var f = get();
      Assert.That(f.CellWidth, Is.EqualTo(cellW), $"{label}: cellW mismatch");
      Assert.That(f.CellHeight, Is.EqualTo(cellH), $"{label}: cellH mismatch");
      Assert.That(f.GlyphData.Length, Is.EqualTo(256 * cellH), $"{label}: glyph buffer size mismatch");
    }
  }

  [Test]
  [Category("Unit")]
  public void All_HasSevenEntries() {
    Assert.That(BitmapFontEmbedded.All, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void IbmVga_AGlyph_MatchesCanonicalRomDump() {
    // The IBM VGA 'A' glyph (codepoint 0x41) has well-known canonical bytes from the VGA BIOS ROM.
    // Failure here means we either lost or replaced the authentic ROM dump.
    var vga = BitmapFontEmbedded.IbmVga8x16;
    byte[] canonicalA = [
      0x00, 0x00, 0x10, 0x38, 0x6C, 0xC6, 0xC6, 0xFE,
      0xC6, 0xC6, 0xC6, 0xC6, 0x00, 0x00, 0x00, 0x00,
    ];
    for (var r = 0; r < 16; ++r)
      Assert.That(vga.GetGlyphRow(0x41, r), Is.EqualTo(canonicalA[r]), $"row {r}");
  }
}
