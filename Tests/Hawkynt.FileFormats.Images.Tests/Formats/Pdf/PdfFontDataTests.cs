using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Pdf.Fonts.Data;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class PdfFontDataTests {

  [Test]
  [Category("Unit")]
  public void Encodings_NameTheCodesAnnexDGivesThem() {
    Assert.Multiple(() => {
      Assert.That(PdfEncodings.ByName("WinAnsiEncoding")![0x80], Is.EqualTo("Euro"));
      Assert.That(PdfEncodings.ByName("WinAnsiEncoding")![0x41], Is.EqualTo("A"));
      Assert.That(PdfEncodings.ByName("StandardEncoding")![0x27], Is.EqualTo("quoteright"));
      Assert.That(PdfEncodings.ByName("StandardEncoding")![0x60], Is.EqualTo("quoteleft"));
      Assert.That(PdfEncodings.ByName("MacRomanEncoding")![0xDB], Is.EqualTo("currency"));
      Assert.That(PdfEncodings.ByName("MacRomanEncoding")![0x80], Is.EqualTo("Adieresis"));
      Assert.That(PdfEncodings.ByName("SymbolSetEncoding")![0x61], Is.EqualTo("alpha"));
      Assert.That(PdfEncodings.ByName("ZapfDingbatsEncoding")![0x21], Is.EqualTo("a1"));
      Assert.That(PdfEncodings.ByName("MacExpertEncoding")![0x20], Is.EqualTo("space"));
      Assert.That(PdfEncodings.ByName("PDFDocEncoding")![0x80], Is.EqualTo("bullet"));
      Assert.That(PdfEncodings.ByName("PDFDocEncoding")![0xA0], Is.EqualTo("Euro"));
      Assert.That(PdfEncodings.ByName("PDFDocEncoding")![0x18], Is.EqualTo("breve"));
      Assert.That(PdfEncodings.ByName("PDFDocEncoding")![0x9F], Is.Null);
      Assert.That(PdfEncodings.ByName("StandardEncoding")![0x00], Is.Null);
      Assert.That(PdfEncodings.ByName("NoSuchEncoding"), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void Encodings_AreFreshCopies_SoDifferencesDoNotLeak() {
    var first = PdfEncodings.ByName("WinAnsiEncoding")!;
    first[0x41] = "Alpha";
    Assert.Multiple(() => {
      Assert.That(first, Has.Length.EqualTo(256));
      Assert.That(PdfEncodings.ByName("WinAnsiEncoding")![0x41], Is.EqualTo("A"));
    });
  }

  [Test]
  [Category("Unit")]
  public void GlyphList_MapsNamesToCodePoints() {
    Assert.Multiple(() => {
      Assert.That(GlyphList.UnicodeOf("Aring"), Is.EqualTo(0xC5));
      Assert.That(GlyphList.UnicodeOf("space"), Is.EqualTo(0x20));
      Assert.That(GlyphList.UnicodeOf("Euro"), Is.EqualTo(0x20AC));
      Assert.That(GlyphList.UnicodeOf("a1"), Is.EqualTo(0x2701));
      Assert.That(GlyphList.UnicodeOf("uni20AC"), Is.EqualTo(0x20AC));
      Assert.That(GlyphList.UnicodeOf("u1F600"), Is.EqualTo(0x1F600));
      Assert.That(GlyphList.UnicodeOf("uni20ac"), Is.EqualTo(-1));
      Assert.That(GlyphList.UnicodeOf("uniD800"), Is.EqualTo(-1));
      Assert.That(GlyphList.UnicodeOf("a.sc"), Is.EqualTo('a'));
      Assert.That(GlyphList.UnicodeOf("notaglyphname"), Is.EqualTo(-1));
      Assert.That(GlyphList.UnicodeOf(""), Is.EqualTo(-1));
    });
  }

  [Test]
  [Category("Unit")]
  public void GlyphList_NamesCodePoints() {
    Assert.Multiple(() => {
      Assert.That(GlyphList.NameOf(0x20), Is.EqualTo("space"));
      Assert.That(GlyphList.NameOf(0xC5), Is.EqualTo("Aring"));
      Assert.That(GlyphList.NameOf(0x2022), Is.EqualTo("bullet"));
      Assert.That(GlyphList.NameOf(0x10FFFF), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void Metrics_AreTheAfmWidths() {
    Assert.Multiple(() => {
      Assert.That(StandardFontMetrics.TryGetWidth("Helvetica", "A", out var helveticaA) ? helveticaA : -1, Is.EqualTo(667));
      Assert.That(StandardFontMetrics.TryGetWidth("Times-Roman", "space", out var timesSpace) ? timesSpace : -1, Is.EqualTo(250));
      Assert.That(StandardFontMetrics.TryGetWidth("Courier", "A", out var courierA) ? courierA : -1, Is.EqualTo(600));
      Assert.That(StandardFontMetrics.TryGetWidth("Courier-BoldOblique", "anything", out var courierAny) ? courierAny : -1, Is.EqualTo(600));
      Assert.That(StandardFontMetrics.TryGetWidth("Helvetica-Bold", "W", out var helveticaBoldW) ? helveticaBoldW : -1, Is.EqualTo(944));
      Assert.That(StandardFontMetrics.TryGetWidth("Symbol", "alpha", out var alpha) ? alpha : -1, Is.EqualTo(631));
      Assert.That(StandardFontMetrics.TryGetWidth("ZapfDingbats", "a1", out var a1) ? a1 : -1, Is.EqualTo(974));
      Assert.That(StandardFontMetrics.TryGetWidth("Helvetica", "nosuchglyph", out _), Is.False);
      Assert.That(StandardFontMetrics.TryGetWidth("Arial", "A", out _), Is.False);
      Assert.That(StandardFontMetrics.IsStandard("Times-BoldItalic"), Is.True);
      Assert.That(StandardFontMetrics.IsStandard("Arial"), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void Names_NormaliseToTheStandardFourteen() {
    Assert.Multiple(() => {
      Assert.That(StandardFontNames.Canonical("Arial,BoldItalic", 0, false, false), Is.EqualTo("Helvetica-BoldOblique"));
      Assert.That(StandardFontNames.Canonical("TimesNewRomanPSMT", 0, false, false), Is.EqualTo("Times-Roman"));
      Assert.That(StandardFontNames.Canonical("ABCDEF+CourierNewPS-BoldMT", 0, false, false), Is.EqualTo("Courier-Bold"));
      Assert.That(StandardFontNames.Canonical("Helvetica", 0, false, false), Is.EqualTo("Helvetica"));
      Assert.That(StandardFontNames.Canonical("Symbol", 0, true, false), Is.EqualTo("Symbol"));
      Assert.That(StandardFontNames.Canonical("Times-Roman,Bold", 0, false, false), Is.EqualTo("Times-Bold"));
      Assert.That(StandardFontNames.Canonical("Arial", 1 << 18, false, false), Is.EqualTo("Helvetica-Bold"));
      Assert.That(StandardFontNames.Canonical("Calibri-Italic", 0, false, false), Is.EqualTo("Helvetica-Oblique"));
      Assert.That(StandardFontNames.Canonical("Garamond", 0, false, false), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void Fallback_FollowsTheFlags() {
    Assert.Multiple(() => {
      Assert.That(StandardFontNames.Fallback(1, false, false), Is.EqualTo("Courier"));
      Assert.That(StandardFontNames.Fallback(2, true, true), Is.EqualTo("Times-BoldItalic"));
      Assert.That(StandardFontNames.Fallback(0, false, true), Is.EqualTo("Helvetica-Oblique"));
      Assert.That(StandardFontNames.Fallback("Baskerville-Bold", 0, true, false), Is.EqualTo("Times-Bold"));
      Assert.That(StandardFontNames.Fallback("Futura", 0, false, false), Is.EqualTo("Helvetica"));
    });
  }

  [TestCase("Helvetica", "Liberation Sans")]
  [TestCase("Helvetica-Bold", "Liberation Sans")]
  [TestCase("Helvetica-Oblique", "Liberation Sans")]
  [TestCase("Helvetica-BoldOblique", "Liberation Sans")]
  [TestCase("Times-Roman", "Liberation Serif")]
  [TestCase("Times-Bold", "Liberation Serif")]
  [TestCase("Times-Italic", "Liberation Serif")]
  [TestCase("Times-BoldItalic", "Liberation Serif")]
  [TestCase("Courier", "Liberation Mono")]
  [TestCase("Courier-Bold", "Liberation Mono")]
  [TestCase("Courier-Oblique", "Liberation Mono")]
  [TestCase("Courier-BoldOblique", "Liberation Mono")]
  [Category("Unit")]
  public void Substitutes_AreTheExpectedTrueTypeFamily(string standardName, string family) {
    var font = SubstituteFonts.TrueTypeFor(standardName);
    Assert.That(font, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(font!), Is.EqualTo(0x00010000u));
      Assert.That(_FamilyName(font!), Is.EqualTo(family));
      Assert.That(SubstituteFonts.TrueTypeFor(standardName), Is.SameAs(font));
    });
  }

  [Test]
  [Category("Unit")]
  public void Substitutes_LeaveOutTheSymbolFonts() {
    Assert.Multiple(() => {
      Assert.That(SubstituteFonts.TrueTypeFor("Symbol"), Is.Null);
      Assert.That(SubstituteFonts.TrueTypeFor("ZapfDingbats"), Is.Null);
      Assert.That(SubstituteFonts.Omitted, Is.EquivalentTo(new[] { "Symbol", "ZapfDingbats" }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Substitutes_CarryTheirLicence() {
    using var stream = typeof(SubstituteFonts).Assembly.GetManifestResourceStream("FileFormat.Pdf.Fonts.Data.Substitutes.OFL-Liberation.txt");
    Assert.That(stream, Is.Not.Null);
    using var reader = new System.IO.StreamReader(stream!);
    Assert.That(reader.ReadToEnd(), Does.Contain("SIL OPEN FONT LICENSE Version 1.1"));
  }

  /// <summary>Name ID 1 from the Windows Unicode record of a TrueType name table.</summary>
  private static string? _FamilyName(byte[] font) {
    var tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
    for (var i = 0; i < tables; ++i) {
      var record = 12 + i * 16;
      if (Encoding.ASCII.GetString(font, record, 4) != "name")
        continue;

      var table = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
      var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(table + 2));
      var strings = table + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(table + 4));
      for (var n = 0; n < count; ++n) {
        var entry = font.AsSpan(table + 6 + n * 12);
        if (BinaryPrimitives.ReadUInt16BigEndian(entry) != 3 || BinaryPrimitives.ReadUInt16BigEndian(entry[6..]) != 1)
          continue;

        var length = BinaryPrimitives.ReadUInt16BigEndian(entry[8..]);
        var offset = BinaryPrimitives.ReadUInt16BigEndian(entry[10..]);
        return Encoding.BigEndianUnicode.GetString(font, strings + offset, length);
      }
    }

    return null;
  }
}
