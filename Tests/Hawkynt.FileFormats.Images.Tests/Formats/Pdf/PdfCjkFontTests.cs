using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Pdf.Fonts;
using FileFormat.Pdf.Fonts.Data;
using FileFormat.Pdf.Rendering;

namespace FileFormat.Pdf.Tests;

/// <summary>Non-embedded CJK CID fonts drawn with an installed font, held to poppler given the same fonts.</summary>
/// <remarks>
/// The CI tests use only the fonts in <c>Fixtures/CjkFonts</c>: two faces cut from Noto Sans CJK and
/// one from Noto Serif CJK (SIL OFL 1.1) by <c>make_cjk_fonts.py</c>, with system fonts turned off,
/// so they do not depend on what the machine has installed. <c>Render/cjk.poppler.png</c> is pdftoppm
/// 26.11 (<c>-r 144 -cropbox -aa yes -aaVector yes</c>) with <c>FONTCONFIG_FILE</c> pointing at a
/// configuration whose only font folder is that one, with aliases sending MS Gothic, MS Mincho and
/// STHeiti to the faces this renderer picks for them — so the comparison is of the glyphs each CID
/// becomes and where they land, not of two font matchers.
/// </remarks>
[TestFixture]
public sealed class PdfCjkFontTests {

  private static string _FontFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdf", "CjkFonts");

  private static PdfRenderOptions _Fixture(double resolution = 72) => new() { Resolution = resolution, UseSystemFonts = false, FontDirectories = [_FontFolder] };

  private static PdfSystemFonts _FixtureFonts => PdfSystemFonts.For([_FontFolder], false)!;

  private static string _Stream(string content) => $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";

  [Test]
  [Category("Unit")]
  public void NonEmbeddedCjkFixture_AgreesWithPoppler() {
    var file = PdfReader.FromBytes(PdfDocumentTests.Fixture("Render/cjk.pdf"));
    var rendered = PdfFile.RenderPage(file, 0, _Fixture(144));
    var reference = PdfRenderComparison.ReadPng(PdfDocumentTests.Fixture("Render/cjk.poppler.png"));
    var comparison = PdfRenderComparison.Of(rendered.Image, reference);
    Assert.Multiple(() => {
      Assert.That(comparison.SameSize, Is.True);
      Assert.That(comparison.MeanAbsoluteError, Is.LessThanOrEqualTo(0.6), comparison.ToString());
      Assert.That(comparison.BadPercent, Is.LessThanOrEqualTo(0.1), comparison.ToString());
      Assert.That(rendered.Omissions, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void FontFolder_YieldsEachFaceWithItsRegionAndCoverage() {
    var faces = _FixtureFonts.Faces.OrderBy(f => f.Family, StringComparer.Ordinal).ToArray();
    Assert.That(faces.Select(f => f.Family), Is.EqualTo(new[] { "Fixture Sans CJK JP", "Fixture Sans CJK SC", "Fixture Serif CJK JP" }));
    Assert.Multiple(() => {
      Assert.That(faces.Select(f => f.Region), Is.EqualTo(new[] { "Japan1", "GB1", "Japan1" }));
      Assert.That(faces.Select(f => f.IsSerif), Is.EqualTo(new bool?[] { false, false, true }));
      Assert.That(faces.Select(f => f.Index), Is.EqualTo(new[] { 0, 1, 0 }), "the two Sans faces are one collection");
      Assert.That(faces[0].Covers("Japan1") && !faces[0].Covers("Korea1"), Is.True);
      Assert.That(faces[1].Covers("GB1") && !faces[1].Covers("CNS1"), Is.True);
    });
  }

  [TestCase("Japan1", "MS-Gothic", false, "Fixture Sans CJK JP")]
  [TestCase("Japan1", "KozMinPr6N-Regular", true, "Fixture Serif CJK JP")]
  [TestCase("GB1", "STSong-Light", true, "Fixture Sans CJK SC")]
  [TestCase("CNS1", "MSung-Light", true, null)]
  [TestCase("Korea1", "HYGoThic-Medium", false, null)]
  [Category("Unit")]
  public void Find_PrefersTheRegionThenTheStyle_AndFindsNothingForAnUncoveredCollection(string ordering, string name, bool serif, string? expected) {
    var face = _FixtureFonts.Find(ordering, name, serif, false, false);
    Assert.That(face?.Face.Family, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void CidToUnicode_TurnsAdobesCMapsAround() {
    var japan = PdfCidToUnicode.For("Japan1")!;
    Assert.Multiple(() => {
      // UniJIS-UCS2-H: <3042> 843 and <3001> <3002> 634; UniJIS-UCS2-V: <3001> <3002> 7887;
      // UniJIS-UTF32-H: <0000fe11> 7887, the presentation form of the vertical comma.
      Assert.That(japan.Horizontal(843, 0), Is.EqualTo(0x3042));
      Assert.That(japan.Horizontal(634, 0), Is.EqualTo(0x3001));
      Assert.That(japan.Horizontal(7887, 0), Is.EqualTo(0xFE11));
      Assert.That(japan.Vertical(7887), Is.EqualTo(0x3001));
      // Adobe-Japan1-UCS2: <3e8c> <a5> — a CID no Unicode CMap reaches.
      Assert.That(japan.Adobe(843), Is.EqualTo(0x3042));
      Assert.That(PdfCidToUnicode.For("Identity"), Is.Null);
    });

    foreach (var (ordering, cmap, character) in new[] { ("GB1", "UniGB-UCS2-H", 0x4E2D), ("CNS1", "UniCNS-UCS2-H", 0x570B), ("Korea1", "UniKS-UCS2-H", 0xAC00) }) {
      var cid = PdfCMap.Predefined(cmap)!.Cid((uint)character, 2);
      Assert.That(PdfCidToUnicode.For(ordering)!.Horizontal(cid, 0), Is.EqualTo(character), ordering);
    }
  }

  [Test]
  [Category("Unit")]
  public void ThroughUnicode_ACidReachesTheCmapGlyph_AndAVerticalFormItsVerticalAlternate() {
    var face = _FixtureFonts.Find("Japan1", "MS-Gothic", false, false, false)!;
    var substitute = new PdfCidSubstitute(face, PdfCidToUnicode.For("Japan1")!, verticalWriting: true);
    var comma = face.Cmap.Lookup(0x3001);
    var verticalComma = face.VerticalForms("kana", "JAN ")!.Substitute(comma);
    Assert.Multiple(() => {
      Assert.That(substitute.ByCid, Is.False, "the face is keyed by Adobe-Identity-0");
      Assert.That(substitute.GlyphOf(843), Is.EqualTo(face.Cmap.Lookup(0x3042)).And.GreaterThan(0));
      Assert.That(substitute.GlyphOf(634), Is.EqualTo(comma), "the horizontal form stays horizontal in vertical text");
      Assert.That(verticalComma, Is.Not.EqualTo(comma));
      Assert.That(substitute.GlyphOf(7887), Is.EqualTo(verticalComma));
      Assert.That(substitute.GlyphOf(12000), Is.Zero, "a CID the face cannot draw is its .notdef");
    });
  }

  [Test]
  [Category("Unit")]
  public void AFaceKeyedByTheSameCollection_IsReadByCid() {
    var face = _FixtureFonts.Find("Japan1", "MS-Mincho", true, false, false)!;
    var substitute = new PdfCidSubstitute(face, PdfCidToUnicode.For("Japan1")!, verticalWriting: false);
    var cff = (CffFontProgram)face.Program;
    Assert.Multiple(() => {
      Assert.That(face.CharacterCollection, Is.EqualTo(("Adobe", "Japan1", 6)));
      Assert.That(substitute.ByCid, Is.True);
      Assert.That(substitute.GlyphOf(843), Is.EqualTo(cff.GlyphIdOfCid(843)).And.EqualTo(face.Cmap.Lookup(0x3042)));
      Assert.That(substitute.GlyphOf(7887), Is.EqualTo(cff.GlyphIdOfCid(7887)).And.GreaterThan(0));
      Assert.That(substitute.GlyphOf(7887), Is.Not.EqualTo(substitute.GlyphOf(634)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ACollectionNoInstalledFontCovers_IsLeftOut_AndNamed() {
    var pdf = PdfRenderTests._Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /K 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("BT /K 20 Tf 5 5 Td <AC00D55C> Tj ET"),
      "<< /Type /Font /Subtype /Type0 /BaseFont /HYGoThic-Medium /Encoding /UniKS-UCS2-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType0 /BaseFont /HYGoThic-Medium /CIDSystemInfo << /Registry (Adobe) /Ordering (Korea1) /Supplement 2 >> >>] >>");
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0, _Fixture());
    Assert.That(rendering.Omissions, Has.Count.EqualTo(1));
    Assert.That(rendering.Omissions[0], Does.Contain("HYGoThic-Medium").And.Contain("Adobe-Korea1"));
  }

  [Test]
  [Category("Unit")]
  public void BrokenAndForeignFiles_InAFontFolder_ArePassedOver() {
    var folder = Path.Combine(Path.GetTempPath(), "pdf-cjk-broken-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(folder, "nested"));
    try {
      var good = File.ReadAllBytes(Path.Combine(_FontFolder, "FixtureSansCJK.ttc"));
      File.WriteAllBytes(Path.Combine(folder, "empty.ttf"), []);
      File.WriteAllBytes(Path.Combine(folder, "noise.otf"), Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray());
      File.WriteAllBytes(Path.Combine(folder, "truncated.ttc"), good[..600]);
      var lying = (byte[])good.Clone();
      lying[8] = 0x7F; // a collection claiming two billion faces
      File.WriteAllBytes(Path.Combine(folder, "lying.ttc"), lying);
      File.WriteAllBytes(Path.Combine(folder, "nested", "good.TTC"), good);
      File.WriteAllText(Path.Combine(folder, "readme.txt"), "not a font");

      var fonts = PdfSystemFonts.For([folder], false)!;
      Assert.That(fonts.Faces.Select(f => Path.GetFileName(f.Path)).Distinct(), Is.EquivalentTo(new[] { "good.TTC", "lying.ttc" }));
      Assert.That(fonts.Find("Japan1", "MS-Gothic", false, false, false)?.Face.Family, Is.EqualTo("Fixture Sans CJK JP"));
    } finally {
      Directory.Delete(folder, true);
    }
  }

  [Test]
  [Category("Unit")]
  public void SystemFontsTurnedOff_WithNoFolders_SearchesNothing() {
    Assert.That(PdfSystemFonts.For([], false), Is.Null);
    var pdf = PdfRenderTests._Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /J 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("BT /J 20 Tf 5 5 Td <30423044> Tj ET"),
      "<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /UniJIS-UCS2-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>] >>");
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0, new PdfRenderOptions { UseSystemFonts = false });
    Assert.That(rendering.Omissions.Single(), Does.Contain("installed fonts are not searched"));
  }

  [Test]
  [Category("Unit")]
  public void FontconfigDirectories_ResolveTheirPrefixes() {
    var file = Path.Combine(Path.GetTempPath(), "pdf-fonts-" + Guid.NewGuid().ToString("N") + ".conf");
    File.WriteAllText(file, "<fontconfig>\n<dir>/opt/fonts</dir>\n<dir prefix=\"xdg\">fonts</dir>\n<dir>~/.myfonts</dir>\n<dir prefix=\"relative\">local</dir>\n<cachedir>/var/cache</cachedir>\n</fontconfig>");
    try {
      var directories = PdfSystemFonts.FontconfigDirectories(file, "/home/someone", "/home/someone/.local/share").ToArray();
      Assert.That(directories, Is.EqualTo(new[] { "/opt/fonts", Path.Combine("/home/someone/.local/share", "fonts"), "/home/someone/.myfonts", Path.Combine(Path.GetDirectoryName(file)!, "local") }));
    } finally {
      File.Delete(file);
    }
  }

  /// <summary>
  /// Every collection, horizontal and vertical, drawn with real installed CJK fonts and held to
  /// pdftoppm given the same fonts.
  /// </summary>
  /// <remarks>
  /// <c>PDF_CJK_FONTS</c> names a folder of CJK fonts — Noto Sans CJK and Noto Serif CJK cover all four
  /// collections in both styles. The renderer searches that folder alone; pdftoppm runs with a private
  /// fontconfig file listing it, with an alias sending each PDF font's family to the face the renderer
  /// chose. Each collection gets a page of its serif and sans fonts through its Uni…-UCS2-H and -V
  /// CMaps and through Identity-H and -V over CID ranges that include the vertical forms.
  /// </remarks>
  [Test]
  [Explicit("Needs CJK fonts in PDF_CJK_FONTS and poppler's pdftoppm.")]
  [Category("Regression")]
  public void InstalledCjkFonts_AgreeWithPoppler() {
    var folder = Environment.GetEnvironmentVariable("PDF_CJK_FONTS");
    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
      Assert.Ignore("Set PDF_CJK_FONTS to a folder of CJK fonts.");

    var fonts = PdfSystemFonts.For([folder!], false)!;
    var work = Path.Combine(Path.GetTempPath(), "pdf-cjk-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    var report = new StringBuilder();
    var failures = 0;
    try {
      foreach (var (ordering, uni, serifName, sansName, text, cids) in _Collections) {
        var aliases = new List<(string Family, string Face)>();
        foreach (var (name, serif) in new[] { (serifName, true), (sansName, false) }) {
          var face = fonts.Find(ordering, name, serif, false, false);
          if (face == null) {
            report.AppendLine($"SKIP    {ordering}: no font in the folder covers it");
            continue;
          }

          aliases.Add((_PopplerFamily(name), face.Face.Family));
        }

        if (aliases.Count < 2)
          continue;

        var pdf = Path.Combine(work, $"cjk-{ordering}.pdf");
        File.WriteAllBytes(pdf, _CollectionPage(ordering, uni, serifName, sansName, text, cids));
        var config = Path.Combine(work, $"fonts-{ordering}.conf");
        File.WriteAllText(config, _FontconfigFile(folder!, Path.Combine(work, "cache"), aliases));
        foreach (var resolution in new[] { 72.0, 150.0 }) {
          var rendering = PdfFile.RenderPage(PdfReader.FromBytes(File.ReadAllBytes(pdf)), 0, new PdfRenderOptions { Resolution = resolution, UseSystemFonts = false, FontDirectories = [folder!] });
          var reference = _Poppler(pdf, resolution, config);
          if (reference == null) {
            report.AppendLine($"SKIP    {ordering} at {resolution} dpi: pdftoppm produced nothing");
            continue;
          }

          var comparison = PdfRenderComparison.Of(rendering.Image, reference);
          var ok = comparison.SameSize && comparison.MeanAbsoluteError <= 1.5 && comparison.BadPercent <= 1 && rendering.Omissions.Count == 0;
          report.AppendLine($"{(ok ? "PASS" : "FAIL"),-7} {ordering} at {resolution} dpi ({string.Join(", ", aliases.Select(a => a.Face))}): {comparison}{(rendering.Omissions.Count > 0 ? " omitted: " + string.Join("; ", rendering.Omissions) : "")}");
          if (!ok)
            ++failures;
        }
      }
    } finally {
      try {
        Directory.Delete(work, true);
      } catch (IOException) {
      }
    }

    TestContext.Out.WriteLine(report.ToString());
    Assert.That(failures, Is.Zero, report.ToString());
  }

  private static readonly (string Ordering, string Uni, string Serif, string Sans, string Text, (int Low, int High)[] Cids)[] _Collections = [
    ("Japan1", "UniJIS", "MS-Mincho", "MS-Gothic", "日本語のテキスト、縦書き。「ー」ＡＢＣ（漢字）ABCabc123", [(1, 40), (230, 260), (633, 660), (842, 870), (1125, 1150), (7887, 7940), (8268, 8290), (12870, 12890)]),
    ("GB1", "UniGB", "STSong-Light", "STHeiti-Regular", "中文简体字，这是测试。“国”（汉字）ABCabc123", [(1, 40), (600, 640), (814, 830), (1000, 1030), (4000, 4030), (22353, 22380)]),
    ("CNS1", "UniCNS", "MSung-Light", "MHei-Medium", "繁體中文，這是測試。「國」（漢字）ABCabc123", [(1, 40), (99, 130), (595, 640), (5401, 5430), (13648, 13700), (14000, 14030)]),
    ("Korea1", "UniKS", "HYSMyeongJo-Medium", "HYGoThic-Medium", "한국어 텍스트입니다. 漢字「」（）ABCabc123", [(1, 40), (101, 140), (1000, 1040), (3000, 3030), (8094, 8130), (8700, 8720)]),
  ];

  // The family poppler asks fontconfig for: the name up to its first hyphen when a weight or style
  // word follows, hyphens turned into spaces.
  private static string _PopplerFamily(string name) {
    var cut = name.IndexOf('-');
    var rest = cut >= 0 ? name[cut..] : "";
    var modifier = new[] { "Regular", "Roman", "Oblique", "Italic", "Bold", "Light", "Medium", "Condensed" }.Any(m => rest.Contains(m, StringComparison.Ordinal));
    return (modifier ? name[..cut] : name).Replace('-', ' ');
  }

  private static string _FontconfigFile(string folder, string cache, IEnumerable<(string Family, string Face)> aliases) {
    var text = new StringBuilder("<?xml version=\"1.0\"?>\n<!DOCTYPE fontconfig SYSTEM \"urn:fontconfig:fonts.dtd\">\n<fontconfig>\n");
    text.Append($"  <dir>{System.Security.SecurityElement.Escape(folder)}</dir>\n  <cachedir>{System.Security.SecurityElement.Escape(cache)}</cachedir>\n");
    foreach (var (family, face) in aliases)
      text.Append($"  <alias binding=\"strong\"><family>{System.Security.SecurityElement.Escape(family)}</family><prefer><family>{System.Security.SecurityElement.Escape(face)}</family></prefer></alias>\n");
    return text.Append("</fontconfig>\n").ToString();
  }

  private static FileFormat.Core.RawImage? _Poppler(string pdf, double resolution, string fontconfig) {
    var prefix = Path.Combine(Path.GetDirectoryName(pdf)!, Path.GetFileNameWithoutExtension(pdf) + "-" + resolution.ToString(CultureInfo.InvariantCulture));
    var start = new ProcessStartInfo("pdftoppm", string.Format(CultureInfo.InvariantCulture, "-r {0} -singlefile -cropbox -aa yes -aaVector yes -png \"{1}\" \"{2}\"", resolution, pdf, prefix)) {
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };
    start.Environment["FONTCONFIG_FILE"] = fontconfig;
    try {
      using var process = Process.Start(start);
      if (process == null)
        return null;
      process.StandardError.ReadToEnd();
      process.StandardOutput.ReadToEnd();
      if (!process.WaitForExit(300_000))
        return null;
    } catch (System.ComponentModel.Win32Exception) {
      return null;
    }

    return File.Exists(prefix + ".png") ? PdfRenderComparison.ReadPng(File.ReadAllBytes(prefix + ".png")) : null;
  }

  private static byte[] _CollectionPage(string ordering, string uni, string serifName, string sansName, string text, (int Low, int High)[] cids) {
    var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "", "" };
    var fonts = new Dictionary<(bool Serif, string Encoding), int>();

    int Add(string body) {
      objects.Add(body);
      return objects.Count;
    }

    foreach (var (serif, name) in new[] { (true, serifName), (false, sansName) }) {
      var descriptor = Add($"<< /Type /FontDescriptor /FontName /{name} /Flags {(serif ? 6 : 4)} /FontBBox [0 -141 1000 859] /ItalicAngle 0 /Ascent 859 /Descent -141 /CapHeight 700 /StemV 80 >>");
      var cidFont = Add($"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement 2 >> /FontDescriptor {descriptor} 0 R /DW 1000 /W [1 95 500 231 632 500] /DW2 [880 -1000] >>");
      foreach (var encoding in new[] { uni + "-UCS2-H", "Identity-H", uni + "-UCS2-V", "Identity-V" })
        fonts[(serif, encoding)] = Add($"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /{encoding} /DescendantFonts [{cidFont} 0 R] >>");
    }

    var names = fonts.Keys.Select((key, i) => (key, name: "/F" + i)).ToDictionary(e => e.key, e => e.name);
    var resources = "<< /Font << " + string.Join(" ", fonts.Select(f => $"{names[f.Key]} {f.Value} 0 R")) + " >> >>";
    string Utf16(string s) => "<" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(s)) + ">";
    string Cids(IEnumerable<(int Low, int High)> ranges) => "<" + string.Concat(ranges.SelectMany(r => Enumerable.Range(r.Low, r.High - r.Low + 1)).Select(c => c.ToString("X4", CultureInfo.InvariantCulture))) + ">";

    var content = new StringBuilder("BT\n");
    var y = 800;
    foreach (var serif in new[] { true, false }) {
      content.Append($"{names[(serif, uni + "-UCS2-H")]} 16 Tf 1 0 0 1 20 {y} Tm {Utf16(text)} Tj\n");
      y -= 26;
      var half = cids.Length / 2;
      foreach (var part in new[] { cids[..half], cids[half..] }) {
        content.Append($"{names[(serif, "Identity-H")]} 9 Tf 1 0 0 1 20 {y} Tm {Cids(part)} Tj\n");
        y -= 16;
      }

      y -= 8;
    }

    var x = 40;
    foreach (var serif in new[] { true, false }) {
      content.Append($"{names[(serif, uni + "-UCS2-V")]} 16 Tf 1 0 0 1 {x} 520 Tm {Utf16(text)} Tj\n");
      x += 30;
      foreach (var (low, high) in cids) {
        content.Append($"{names[(serif, "Identity-V")]} 9 Tf 1 0 0 1 {x} 520 Tm {Cids([(low, Math.Min(high, low + 30))])} Tj\n");
        x += 14;
      }

      x += 20;
    }

    content.Append("ET\n");
    objects[2] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources {resources} /Contents 4 0 R >>";
    objects[3] = $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";
    return PdfRenderTests._Build([.. objects]);
  }
}
