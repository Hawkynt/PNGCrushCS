using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Pdf.Fonts;

namespace FileFormat.Pdf.Tests;

/// <summary>
/// The embedded font program parsers, against fixtures drawn for the purpose and measured by
/// fontTools (outline bounds and signed area through its BoundsPen and AreaPen) and Ghostscript
/// (Type 1 advance widths, flex and seac placement).
/// </summary>
/// <remarks>
/// The fixtures were built with fontTools from hand-written charstrings and glyf contours, so each
/// one exercises a known set of operators: hintmask byte counts, local and global subroutines,
/// flex in all four Type 2 forms, endchar seac, the predefined and custom charsets and encodings,
/// a CID-keyed font with two FDs of different matrices; for Type 1, eexec in binary and in hex,
/// OtherSubrs flex and hint replacement, div, sbw and seac; for TrueType, contours that start off
/// the curve, composites with offsets, scales and a 2×2 transform, cmap formats 4 and 12 and post
/// format 2 names. Where fontTools and Ghostscript disagree — fontTools reports zero for the width
/// sbw sets — Ghostscript's answer is the one expected here.
/// </remarks>
[TestFixture]
public sealed class PdfFontProgramTests {

  private const double _BoundsTolerance = 1e-3;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdf", "Fonts", name);
    if (!File.Exists(path))
      Assert.Fail($"Fixture {name} was not copied beside the tests.");

    return File.ReadAllBytes(path);
  }

  private static void _AssertGlyph(IPdfFontProgram font, int gid, double[]? bounds, double area, double width) {
    var outline = font.Outline(gid);
    Assert.That(outline, Is.Not.Null, $"glyph {gid}");
    var (measured, measuredArea) = Measure(outline!);
    Assert.Multiple(() => {
      if (bounds == null)
        Assert.That(measured, Is.Null, $"glyph {gid} should draw nothing");
      else {
        Assert.That(measured, Is.Not.Null, $"glyph {gid} should draw something");
        var (minX, minY, maxX, maxY) = measured!.Value;
        Assert.That(new[] { minX, minY, maxX, maxY }, Is.EqualTo(bounds).Within(_BoundsTolerance), $"glyph {gid} bounds");
      }

      Assert.That(measuredArea, Is.EqualTo(area).Within(1e-4 + Math.Abs(area) * 0.005), $"glyph {gid} area");
      Assert.That(font.AdvanceWidth(gid), Is.EqualTo(width).Within(1e-6), $"glyph {gid} width");
    });
  }

  #region CFF

  [Test]
  [Category("Unit")]
  public void Cff_NameKeyed_OutlinesMatchFontTools() {
    var font = CffFontProgram.Parse(_Fixture("name-keyed.cff"));
    Assert.That(font.GlyphCount, Is.EqualTo(10));
    Assert.That(font.IsCidKeyed, Is.False);

    _AssertGlyph(font, 0, null, 0, 0.5);
    _AssertGlyph(font, 1, null, 0, 0.25);
    _AssertGlyph(font, 2, [0.05, 0, 0.65, 0.7], 0.205, 0.6);         // hstemhm, vstemhm, hintmask
    _AssertGlyph(font, 3, [0.15, 0, 0.55, 0.4], 0.1385, 0.7);        // callsubr, callgsubr, hv/vhcurveto
    _AssertGlyph(font, 4, [0.05, 0, 0.25, 0.7], 0.14, 0.3);          // hlineto alternation
    _AssertGlyph(font, 5, [0.05, 0.2625, 0.95, 0.5], 0.0324, 0.65);  // flex, hflex
    _AssertGlyph(font, 6, [0.05, 0.1, 0.6, 0.23], 0.03768, 0.65);    // flex1, hflex1
    _AssertGlyph(font, 7, [0.05, 0.05, 0.67, 0.55], -0.016475, 0.55); // rcurveline, rlinecurve, vv/hhcurveto
    _AssertGlyph(font, 8, [0.1, 0.6, 0.25, 0.75], 0.015, 0.333);
    _AssertGlyph(font, 9, [0.05, 0, 0.65, 0.87], 0.22, 0.6);         // endchar seac: A + acute
  }

  [Test]
  [Category("Unit")]
  public void Cff_NameKeyed_CharsetAndCustomEncoding() {
    var font = CffFontProgram.Parse(_Fixture("name-keyed.cff"));
    Assert.Multiple(() => {
      Assert.That(font.GlyphIdOfName("A"), Is.EqualTo(2));
      Assert.That(font.GlyphIdOfName("Aacute"), Is.EqualTo(9));
      Assert.That(font.GlyphIdOfName("nonexistent"), Is.EqualTo(-1));
      Assert.That(font.GlyphName(3), Is.EqualTo("O"));
      Assert.That(font.GlyphIdOfCid(2), Is.EqualTo(-1));
      Assert.That(font.BuiltInEncoding, Is.EquivalentTo(new Dictionary<int, string> { [65] = "A", [79] = "O", [200] = "Aacute" }));
      Assert.That(font.Outline(10), Is.Null);
      Assert.That(font.Outline(-1), Is.Null);
      Assert.That(font.AdvanceWidth(10), Is.NaN);
    });
  }

  [Test]
  [Category("Unit")]
  public void Cff_CidKeyed_UsesEachFontDictsMatrixAndSubrs() {
    var font = CffFontProgram.Parse(_Fixture("cid-keyed.cff"));
    Assert.That(font.IsCidKeyed, Is.True);
    Assert.That(font.BuiltInEncoding, Is.Null);

    // Odd glyph ids select the second FD, whose matrix is half the first.
    _AssertGlyph(font, 1, null, 0, 0.125);
    _AssertGlyph(font, 2, [0.05, 0, 0.65, 0.7], 0.205, 0.6);
    _AssertGlyph(font, 3, [0.075, 0, 0.275, 0.2], 0.034625, 0.35);
    _AssertGlyph(font, 5, [0.025, 0.13125, 0.475, 0.25], 0.0081, 0.325);
    _AssertGlyph(font, 7, [0.025, 0.025, 0.335, 0.275], -0.00411875, 0.275);
    _AssertGlyph(font, 9, [0.025, 0.025, 0.075, 0.075], 0.00125, 0.3);
  }

  [Test]
  [Category("Unit")]
  public void Cff_CidKeyed_MapsCidsThroughTheCharset() {
    var font = CffFontProgram.Parse(_Fixture("cid-keyed.cff"));
    Assert.Multiple(() => {
      Assert.That(font.GlyphIdOfCid(0), Is.EqualTo(0));
      Assert.That(font.GlyphIdOfCid(34), Is.EqualTo(2));
      Assert.That(font.GlyphIdOfCid(150), Is.EqualTo(9));
      Assert.That(font.GlyphIdOfCid(2), Is.EqualTo(-1));
      Assert.That(font.GlyphIdOfName("A"), Is.EqualTo(-1));
    });
  }

  [Test]
  [Category("Unit")]
  public void OpenTypeWrapper_DelegatesToTheCffTable() {
    var font = TrueTypeFontProgram.ParseOpenType(_Fixture("name-keyed.otf"));
    Assert.That(font, Is.InstanceOf<CffFontProgram>());
    _AssertGlyph(font, 9, [0.05, 0, 0.65, 0.87], 0.22, 0.6);
  }

  [Test]
  [Category("Unit")]
  public void Cff_ShiftedHeader_IsFound() {
    var data = _Fixture("name-keyed.cff");
    var shifted = new byte[data.Length + 3];
    data.CopyTo(shifted, 3);
    var font = CffFontProgram.Parse(shifted);
    _AssertGlyph(font, 2, [0.05, 0, 0.65, 0.7], 0.205, 0.6);
  }

  [Test]
  [Category("Unit")]
  public void Cff_NotAFont_IsRefused() {
    Assert.Throws<InvalidDataException>(() => CffFontProgram.Parse([0, 0, 0, 0]));
    Assert.Throws<InvalidDataException>(() => CffFontProgram.Parse([]));
  }

  [Test]
  [Category("Unit")]
  public void Cff_TruncatedCharStrings_DrawNothingRatherThanThrow() {
    var data = _Fixture("name-keyed.cff");
    var font = CffFontProgram.Parse(data[..(data.Length - 40)]);
    for (var gid = 0; gid < font.GlyphCount; ++gid)
      Assert.DoesNotThrow(() => font.Outline(gid));
  }

  #endregion

  #region Type 1

  private static readonly (string Name, double[]? Bounds, double Area, double Width)[] _Type1Expected = [
    (".notdef", null, 0, 0.5),
    ("space", null, 0, 0.25),
    ("A", [0.05, 0, 0.65, 0.7], 0.205, 0.6),
    ("O", [0.15, 0, 0.55, 0.4], 0.1385, 0.7),        // hint replacement through OtherSubr 3
    ("F", [0.05, 0.2625, 0.65, 0.4], 0.055, 0.65),   // flex through OtherSubrs 1, 2, 0
    ("I", [0.05, 0, 0.25, 0.7], 0.14, 0.3),
    ("S", [0.05, 0.05, 0.64, 0.56], 0.07, 0.55),     // div, closepath, subroutine
    ("W", [0.05, 0.05, 0.15, 0.15], 0.005, 0.9),     // sbw; width as Ghostscript reports it
    ("acute", [0.1, 0.6, 0.25, 0.75], 0.015, 0.333),
    ("Aacute", [0.05, 0, 0.65, 0.87], 0.22, 0.6),    // seac
  ];

  private static void _AssertType1(Type1FontProgram font) {
    Assert.That(font.GlyphCount, Is.EqualTo(_Type1Expected.Length));
    Assert.That(font.GlyphName(0), Is.EqualTo(".notdef"));
    foreach (var (name, bounds, area, width) in _Type1Expected) {
      var gid = font.GlyphIdOfName(name);
      Assert.That(gid, Is.GreaterThanOrEqualTo(0), name);
      _AssertGlyph(font, gid, bounds, area, width);
    }

    Assert.That(font.BuiltInEncoding, Is.EquivalentTo(new Dictionary<int, string> { [32] = "space", [65] = "A", [70] = "F", [79] = "O", [200] = "Aacute" }));
    Assert.That(font.FontMatrix, Is.EqualTo(new[] { 0.001, 0, 0, 0.001, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void Type1_BinaryEexec_WithStreamLengths() => _AssertType1(Type1FontProgram.Parse(_Fixture("type1.pfa"), 481, 973));

  [Test]
  [Category("Unit")]
  public void Type1_BinaryEexec_WithoutLengths() => _AssertType1(Type1FontProgram.Parse(_Fixture("type1.pfa"), 0, 0));

  [Test]
  [Category("Unit")]
  public void Type1_HexEexec() => _AssertType1(Type1FontProgram.Parse(_Fixture("type1-hex.pfa"), 0, 0));

  /// <summary>
  /// type1-charstrings.cff is type1.pfa recast by make_fixtures.py as a bare CFF with
  /// CharstringType 1 — the same decrypted charstrings and Subrs — so every glyph, flex, hint
  /// replacement and seac included, must come out as the Type 1 program's does.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Cff_WithType1Charstrings_DrawsAsTheType1Program() {
    var font = CffFontProgram.Parse(_Fixture("type1-charstrings.cff"));
    Assert.That(font.GlyphCount, Is.EqualTo(_Type1Expected.Length));
    foreach (var (name, bounds, area, width) in _Type1Expected) {
      var gid = font.GlyphIdOfName(name);
      Assert.That(gid, Is.GreaterThanOrEqualTo(0), name);
      _AssertGlyph(font, gid, bounds, area, width);
    }
  }

  [Test]
  [Category("Unit")]
  public void Type1_PfbSegments() {
    var pfa = _Fixture("type1.pfa");
    var pfb = new List<byte>();
    void Segment(byte type, ReadOnlySpan<byte> body) {
      pfb.AddRange([0x80, type, (byte)body.Length, (byte)(body.Length >> 8), (byte)(body.Length >> 16), (byte)(body.Length >> 24)]);
      pfb.AddRange(body.ToArray());
    }

    Segment(1, pfa.AsSpan(0, 481));
    Segment(2, pfa.AsSpan(481, 973));
    Segment(1, pfa.AsSpan(481 + 973));
    pfb.AddRange([0x80, 3]);
    _AssertType1(Type1FontProgram.Parse(pfb.ToArray(), 0, 0));
  }

  [Test]
  [Category("Unit")]
  public void Type1_WithoutEexec_IsRefused() {
    Assert.Throws<InvalidDataException>(() => Type1FontProgram.Parse("%!PS-AdobeFont-1.0\n/FontName /X def\n"u8.ToArray(), 0, 0));
  }

  #endregion

  #region TrueType

  [Test]
  [Category("Unit")]
  public void TrueType_OutlinesMatchFontTools() {
    var font = TrueTypeFontProgram.Parse(_Fixture("truetype.ttf"));
    Assert.That(font.GlyphCount, Is.EqualTo(7));
    Assert.That(font.BuiltInEncoding, Is.Null);

    _AssertGlyph(font, 0, [0.0488281, 0, 0.439453, 0.683594], 0.267029, 0.634766);
    _AssertGlyph(font, 1, null, 0, 0.244141);
    _AssertGlyph(font, 2, [0.0244141, 0, 0.561523, 0.683594], 0.154972, 0.634766);
    _AssertGlyph(font, 3, [0, 0.341797, 0.585938, 0.683594], 0.175238, 0.634766);       // all points off the curve
    _AssertGlyph(font, 4, [0.0244141, 0, 0.561523, 0.90332], 0.165924, 0.634766);       // composite: offset + scale
    _AssertGlyph(font, 5, [0.171564, 0.341797, 0.772587, 0.683594], 0.175238, 0.634766); // composite: 2x2 transform
    _AssertGlyph(font, 6, [0, 0, 0.488281, 0.488281], 0.198682, 0.634766);
  }

  [Test]
  [Category("Unit")]
  public void TrueType_CmapAndPostNames() {
    var font = TrueTypeFontProgram.Parse(_Fixture("truetype.ttf"));
    Assert.Multiple(() => {
      Assert.That(font.HasCmap(3, 1), Is.True);
      Assert.That(font.HasCmap(3, 10), Is.True);
      Assert.That(font.HasCmap(1, 0), Is.False);
      Assert.That(font.CmapLookup(3, 1, 'A'), Is.EqualTo(2));
      Assert.That(font.CmapLookup(3, 1, 0xC5), Is.EqualTo(4));
      Assert.That(font.CmapLookup(3, 1, 'Z'), Is.EqualTo(-1));
      Assert.That(font.CmapLookup(3, 10, 0x1F600), Is.EqualTo(6));
      Assert.That(font.CmapLookup(3, 10, 'O'), Is.EqualTo(3));
      Assert.That(font.CmapLookup(0, 3, 'O'), Is.EqualTo(3));
      Assert.That(font.CmapLookup(1, 0, 'A'), Is.EqualTo(-1));
      Assert.That(font.GlyphIdOfName("Aring"), Is.EqualTo(4));
      Assert.That(font.GlyphIdOfName("uni1F600"), Is.EqualTo(6));
      Assert.That(font.GlyphIdOfName("nonexistent"), Is.EqualTo(-1));
    });
  }

  [Test]
  [Category("Unit")]
  public void TrueType_TruncatedGlyf_GivesEmptyOutlinesRatherThanThrowing() {
    var data = _Fixture("truetype.ttf");
    // Cut the font off part-way through its tables; whatever survives must still load.
    var font = TrueTypeFontProgram.Parse(data[..(data.Length * 2 / 3)]);
    for (var gid = 0; gid < font.GlyphCount; ++gid)
      Assert.DoesNotThrow(() => font.Outline(gid));
  }

  [Test]
  [Category("Unit")]
  public void TrueType_NoTables_IsRefused() => Assert.Throws<InvalidDataException>(() => TrueTypeFontProgram.Parse(new byte[8]));

  #endregion

  #region installed fonts, where present

  [Test]
  [Category("Unit")]
  public void InstalledTrueType_LiberationSans_ReadsLikeAFont() {
    const string path = "/usr/share/fonts/liberation/LiberationSans-Regular.ttf";
    if (!File.Exists(path))
      Assert.Ignore("Liberation Sans is not installed here.");

    var font = TrueTypeFontProgram.Parse(File.ReadAllBytes(path));
    var gid = font.CmapLookup(3, 1, 'A');
    Assert.That(gid, Is.EqualTo(font.GlyphIdOfName("A")));
    var (bounds, area) = Measure(font.Outline(gid)!);
    Assert.Multiple(() => {
      Assert.That(font.AdvanceWidth(gid), Is.EqualTo(1366 / 2048.0).Within(1e-9));
      Assert.That(bounds!.Value.MaxY, Is.EqualTo(0.6875).Within(0.01));
      Assert.That(Math.Abs(area), Is.GreaterThan(0.05));
    });

    for (var g = 0; g < font.GlyphCount; ++g)
      Assert.DoesNotThrow(() => font.Outline(g));
  }

  [Test]
  [Category("Unit")]
  public void InstalledOpenTypeCff_ReadsEveryGlyph() {
    const string path = "/usr/share/fonts/gsfonts/NimbusSans-Regular.otf";
    if (!File.Exists(path))
      Assert.Ignore("Nimbus Sans is not installed here.");

    var font = TrueTypeFontProgram.ParseOpenType(File.ReadAllBytes(path));
    Assert.That(font, Is.InstanceOf<CffFontProgram>());
    var a = font.GlyphIdOfName("A");
    Assert.That(a, Is.GreaterThan(0));
    Assert.That(font.AdvanceWidth(a), Is.EqualTo(0.667).Within(1e-9));
    Assert.That(Enumerable.Range(0, font.GlyphCount).Count(g => font.Outline(g)!.IsEmpty), Is.LessThan(font.GlyphCount / 10));
  }

  [Test]
  [Category("Unit")]
  public void InstalledType1_GroffSymbol_ReadsEveryGlyph() {
    const string path = "/usr/share/groff/current/font/devpdf/symbolsl.pfb";
    if (!File.Exists(path))
      Assert.Ignore("groff's Type 1 fonts are not installed here.");

    var font = Type1FontProgram.Parse(File.ReadAllBytes(path), 0, 0);
    Assert.That(font.GlyphCount, Is.GreaterThan(1));
    for (var g = 1; g < font.GlyphCount; ++g)
      if (font.GlyphName(g) != "space")
        Assert.That(font.Outline(g)!.IsEmpty, Is.False, font.GlyphName(g));
  }

  #endregion

  /// <summary>The true bounds and the signed area (anticlockwise positive) of an outline, from a fine flattening.</summary>
  internal static ((double MinX, double MinY, double MaxX, double MaxY)? Bounds, double Area) Measure(GlyphOutline outline) {
    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    var area = 0.0;
    var any = false;
    double startX = 0, startY = 0, x = 0, y = 0;
    var p = 0;

    void Visit(double px, double py) {
      minX = Math.Min(minX, px);
      maxX = Math.Max(maxX, px);
      minY = Math.Min(minY, py);
      maxY = Math.Max(maxY, py);
      any = true;
    }

    void Segment(double nx, double ny) {
      Visit(x, y);
      area += x * ny - nx * y;
      x = nx;
      y = ny;
      Visit(x, y);
    }

    var points = outline.Points;
    foreach (var verb in outline.Verbs) {
      switch (verb) {
        case GlyphVerb.MoveTo:
          area += x * startY - startX * y;
          x = startX = points[p++];
          y = startY = points[p++];
          break;
        case GlyphVerb.LineTo:
          Segment(points[p++], points[p++]);
          break;
        case GlyphVerb.CurveTo: {
          double x0 = x, y0 = y, x1 = points[p++], y1 = points[p++], x2 = points[p++], y2 = points[p++], x3 = points[p++], y3 = points[p++];
          for (var i = 1; i <= 64; ++i) {
            var t = i / 64.0;
            var u = 1 - t;
            Segment(
              u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
              u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
          }
          break;
        }
        case GlyphVerb.Close:
          area += x * startY - startX * y;
          x = startX;
          y = startY;
          break;
      }
    }

    area += x * startY - startX * y;
    return (any ? (minX, minY, maxX, maxY) : null, area / 2);
  }
}
