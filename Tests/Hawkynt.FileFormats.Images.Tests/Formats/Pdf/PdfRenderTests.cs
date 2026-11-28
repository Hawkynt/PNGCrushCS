using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Pdf.Document;
using FileFormat.Pdf.Rendering;

namespace FileFormat.Pdf.Tests;

/// <summary>Page rendering, held to poppler's renders of the same self-made fixtures.</summary>
/// <remarks>
/// Each fixture sits beside a <c>.poppler.png</c> that pdftoppm made of it (<c>-cropbox -aa yes
/// -aaVector yes</c>, at 72 dpi unless the case says otherwise), so these run without poppler
/// installed. The bounds are set a little above what the renderer achieves today: they catch a
/// regression, and they record how close agreement is. Where a fixture's bound is loose the case
/// says why.
/// </remarks>
[TestFixture]
public sealed class PdfRenderTests {

  private static PdfFile _Read(string relative) => PdfReader.FromBytes(PdfDocumentTests.Fixture(relative));

  [TestCase("Structure/shapes", 72, 0.6, 0.2)]
  [TestCase("Structure/rot270crop", 72, 0.6, 0.2)]
  [TestCase("Structure/userunit", 72, 0.6, 0.2)]
  [TestCase("Render/vector", 72, 1.5, 0.8)]
  // poppler sizes an axis-aligned image as floor(x0) to floor(x1) + 1, one column and row more than
  // the image reaches; this renderer rounds both edges instead, so a page whose images sit 1:1 on the
  // pixel grid comes back unchanged. That edge is most of the difference here.
  [TestCase("Render/images", 72, 2.5, 0.6)]
  [TestCase("Render/bilevel", 72, 2.5, 1.8)]
  [TestCase("Render/shading", 72, 1.5, 0.8)]
  // The standard fonts are not embedded: poppler draws them in URW's Nimbus faces, this renderer in
  // Liberation, so their lines differ in glyph shape though not in placement.
  [TestCase("Render/text", 144, 2.0, 1.2)]
  [TestCase("Render/transparency", 72, 0.5, 0.2)]
  // ICCBased fills under each rendering intent agree with poppler to the unit; the rest is the
  // resampling of the two upscaled images.
  [TestCase("Render/color", 72, 0.3, 0.1)]
  [TestCase("Render/cmaps", 144, 0.4, 0.2)]
  [TestCase("Render/type1cff", 72, 1.2, 1.0)]
  [TestCase("Render/overprint", 72, 0.3, 0.1)]
  // Symbol and ZapfDingbats are not embedded: poppler draws them in URW's Standard Symbols PS and
  // D050000L, this renderer in its Noto-based substitute, so the glyphs differ in design (and where a
  // Noto glyph is wider than the Adobe advance, in overlap) though not in place.
  [TestCase("Render/symbols", 144, 6.0, 4.0)]
  [Category("Unit")]
  public void Fixture_AgreesWithPoppler(string name, double resolution, double maxMeanError, double maxBadPercent) {
    var file = _Read(name + ".pdf");
    var rendered = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = resolution });
    var reference = PdfRenderComparison.ReadPng(PdfDocumentTests.Fixture(name + ".poppler.png"));
    var comparison = PdfRenderComparison.Of(rendered.Image, reference);
    Assert.Multiple(() => {
      Assert.That(comparison.SameSize, Is.True, $"ours {rendered.Image.Width}x{rendered.Image.Height}, poppler {reference.Width}x{reference.Height}");
      Assert.That(comparison.MeanAbsoluteError, Is.LessThanOrEqualTo(maxMeanError), comparison.ToString());
      Assert.That(comparison.BadPercent, Is.LessThanOrEqualTo(maxBadPercent), comparison.ToString());
      Assert.That(rendered.Omissions, Is.Empty);
    });
  }

  /// <summary>
  /// Fixtures poppler cannot judge, held to Ghostscript's render (png16m, -dUseCropBox, text and
  /// graphics alpha bits 4, at 72 dpi) stored beside them as <c>.ghostscript.png</c>.
  /// </summary>
  /// <remarks>
  /// jpx-alpha: poppler ignores /SMaskInData and reads a four-channel codestream as three-channel
  /// samples. Ghostscript applies the opacity channel; it does not interpolate the upscaled images,
  /// and for /SMaskInData 2 it blends the /Matte colour in instead of removing it (the image at top
  /// right turns green where it fades out), which is where the remaining difference lies.
  /// </remarks>
  [TestCase("Render/jpx-alpha", 72, 3.5, 4.0)]
  [Category("Unit")]
  public void Fixture_AgreesWithGhostscript(string name, double resolution, double maxMeanError, double maxBadPercent) {
    var file = _Read(name + ".pdf");
    var rendered = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = resolution });
    var reference = PdfRenderComparison.ReadPng(PdfDocumentTests.Fixture(name + ".ghostscript.png"));
    var comparison = PdfRenderComparison.Of(rendered.Image, reference);
    Assert.Multiple(() => {
      Assert.That(comparison.SameSize, Is.True);
      Assert.That(comparison.MeanAbsoluteError, Is.LessThanOrEqualTo(maxMeanError), comparison.ToString());
      Assert.That(comparison.BadPercent, Is.LessThanOrEqualTo(maxBadPercent), comparison.ToString());
      Assert.That(rendered.Omissions, Is.Empty);
    });
  }

  /// <summary>
  /// Overprint simulation, held to <c>pdftoppm -overprint -png</c> (poppler 26.11, -cropbox -aa yes
  /// -aaVector yes, 72 dpi) stored as <c>.poppler-overprint.png</c>; without the option the same
  /// page is held to plain pdftoppm, which ignores overprinting, by Fixture_AgreesWithPoppler.
  /// </summary>
  [TestCase("Render/overprint", 72, 0.8, 0.1)]
  [Category("Unit")]
  public void Fixture_WithOverprint_AgreesWithPopplerOverprint(string name, double resolution, double maxMeanError, double maxBadPercent) {
    var file = _Read(name + ".pdf");
    var rendered = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = resolution, SimulateOverprint = true });
    var reference = PdfRenderComparison.ReadPng(PdfDocumentTests.Fixture(name + ".poppler-overprint.png"));
    var comparison = PdfRenderComparison.Of(rendered.Image, reference);
    Assert.Multiple(() => {
      Assert.That(comparison.SameSize, Is.True);
      Assert.That(comparison.MeanAbsoluteError, Is.LessThanOrEqualTo(maxMeanError), comparison.ToString());
      Assert.That(comparison.BadPercent, Is.LessThanOrEqualTo(maxBadPercent), comparison.ToString());
    });
  }

  [Test]
  [Category("Unit")]
  public void JpxOpacity_FollowsSMaskInData() {
    var file = _Read("Render/jpx-alpha.pdf");
    var image = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = 72 }).Image;
    var rgb = image.Format == PixelFormat.Rgb24 ? image : PixelConverter.Convert(image, PixelFormat.Rgb24);
    int[] Pixel(int x, int y) => [rgb.PixelData[(y * rgb.Width + x) * 3], rgb.PixelData[(y * rgb.Width + x) * 3 + 1], rgb.PixelData[(y * rgb.Width + x) * 3 + 2]];
    int[] backdrop = [128, 128, 230];

    Assert.Multiple(() => {
      // The left edge of each image has zero opacity: under 1 and 2 the backdrop shows, under 0 the
      // opacity channel is ignored and the picture's own colour does.
      Assert.That(Pixel(11, 30), Is.EqualTo(backdrop).Within(6), "SMaskInData 1");
      Assert.That(Pixel(111, 30), Is.EqualTo(backdrop).Within(6), "SMaskInData 2 with /Matte");
      Assert.That(Pixel(11, 75), Is.Not.EqualTo(backdrop), "SMaskInData 0");
      // The right edge is opaque under 1.
      Assert.That(Pixel(88, 30), Is.Not.EqualTo(backdrop), "opaque edge");
    });
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_IsThePageCount_AndEachPageHasItsOwnSize() {
    var file = _Read("Structure/tree.pdf");
    Assert.That(PdfFile.ImageCount(file), Is.EqualTo(3));
    var sizes = new List<(int, int)>();
    for (var i = 0; i < 3; ++i) {
      var page = PdfFile.ToRawImage(file, i);
      sizes.Add((page.Width, page.Height));
    }

    Assert.That(sizes, Is.EqualTo(new[] { (120, 80), (60, 90), (90, 60) }));
  }

  [Test]
  [Category("Unit")]
  public void Resolution_ScalesThePage_AndThePixelCapShrinksIt() {
    var file = _Read("Structure/shapes.pdf");
    var doubled = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = 144 }).Image;
    var capped = PdfFile.RenderPage(file, 0, new PdfRenderOptions { Resolution = 720, MaximumPixels = 20_000 }).Image;
    Assert.Multiple(() => {
      Assert.That((doubled.Width, doubled.Height), Is.EqualTo((400, 200)));
      Assert.That((long)capped.Width * capped.Height, Is.LessThanOrEqualTo(20_400));
      Assert.That((double)capped.Width / capped.Height, Is.EqualTo(2).Within(0.05));
    });
  }

  [Test]
  [Category("Unit")]
  public void PageSize_IsTheCeilingOfTheScaledCropBox() {
    var pdf = _OnePage("[0.3 0.7 100.9 51.1]", "0 g 10 10 20 20 re f");
    var image = PdfFile.ToRawImage(PdfReader.FromBytes(pdf), 0);
    Assert.That((image.Width, image.Height), Is.EqualTo((101, 51)));
  }

  [Test]
  [Category("Unit")]
  public void ImageWrittenByThisPackage_ComesBackExactly() {
    var pixels = new byte[7 * 5 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 37 % 251);
    var source = new RawImage { Width = 7, Height = 5, Format = PixelFormat.Rgb24, PixelData = pixels };
    var bytes = PdfWriter.ToBytes(PdfFile.FromRawImage(source));
    var page = PdfFile.ToRawImage(PdfReader.FromBytes(bytes), 0);
    Assert.That(page.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void HiddenOptionalContent_IsNotDrawn() {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] /D << /OFF [5 0 R] >> >> >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Resources << /Properties << /P1 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("/OC /P1 BDC 0 g 0 0 10 10 re f EMC"),
      "<< /Type /OCG /Name (Hidden) >>");
    var page = PdfFile.ToRawImage(PdfReader.FromBytes(pdf), 0);
    Assert.That(page.PixelData, Is.All.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void TextInAFontWithNoSubstitute_IsLeftOut_AndSaidSo() {
    // A CJK CID font that is not embedded: no CJK face is bundled.
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /J 5 0 R /H << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents 4 0 R >>",
      _Stream("BT /J 20 Tf 5 5 Td <30423044> Tj /H 20 Tf (Hi) Tj ET"),
      "<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /UniJIS-UCS2-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>] >>");
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0);
    Assert.Multiple(() => {
      Assert.That(rendering.Omissions, Has.Count.EqualTo(1));
      Assert.That(rendering.Omissions[0], Does.Contain("MS-Mincho"));
      Assert.That(rendering.Image.PixelData, Has.Some.LessThan(128), "the Helvetica text after it is still drawn");
    });
  }

  [Test]
  [Category("Unit")]
  public void NonEmbeddedSymbolAndZapfDingbats_AreDrawnWithTheSubstitute() {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 60] /Resources << /Font << /S << /Type /Font /Subtype /Type1 /BaseFont /Symbol >> /Z << /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >> >> >> /Contents 4 0 R >>",
      _Stream("BT /S 20 Tf 5 35 Td (abg\\346) Tj /Z 20 Tf 0 -30 Td (3456) Tj ET"));
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0);
    var image = rendering.Image.Format == PixelFormat.Rgb24 ? rendering.Image : PixelConverter.Convert(rendering.Image, PixelFormat.Rgb24);
    var top = 0;
    var bottom = 0;
    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x)
        if (image.PixelData[(y * image.Width + x) * 3] < 128) {
          if (y < 30)
            ++top;
          else
            ++bottom;
        }

    Assert.Multiple(() => {
      Assert.That(rendering.Omissions, Is.Empty);
      Assert.That(top, Is.GreaterThan(40), "the Symbol line");
      Assert.That(bottom, Is.GreaterThan(40), "the ZapfDingbats line");
    });
  }

  /// <remarks>
  /// ISO 32000-2 §9.3.6: Type 3 text is drawn by its glyph descriptions in every rendering mode but
  /// 3 and 7, and modes 4 to 7 add nothing to the clipping path. poppler agrees on the clip, but
  /// paints the glyphs in mode 7 too; this follows the specification.
  /// </remarks>
  [TestCase(7, false)]
  [TestCase(4, true)]
  [TestCase(6, true)]
  [Category("Unit")]
  public void Type3TextInAClippingMode_IsDrawnButDoesNotClip(int mode, bool glyphVisible) {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 50] /Resources << /Font << /T 5 0 R >> >> /Contents 4 0 R >>",
      _Stream($"q BT /T 40 Tf {mode} Tr 0 0 1 rg 5 5 Td (a) Tj ET 0 1 0 rg 60 0 40 50 re f Q"),
      "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /sq 6 0 R >> /Encoding << /Type /Encoding /Differences [97 /sq] >> /FirstChar 97 /LastChar 97 /Widths [1000] /Resources << >> >>",
      _Stream("1000 0 0 0 1000 1000 d1 100 100 800 800 re f"));
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0);
    var image = rendering.Image.Format == PixelFormat.Rgb24 ? rendering.Image : PixelConverter.Convert(rendering.Image, PixelFormat.Rgb24);
    int[] Pixel(int x, int y) => [image.PixelData[(y * image.Width + x) * 3], image.PixelData[(y * image.Width + x) * 3 + 1], image.PixelData[(y * image.Width + x) * 3 + 2]];
    Assert.Multiple(() => {
      Assert.That(rendering.Omissions, Is.Empty);
      Assert.That(Pixel(25, 25), Is.EqualTo(glyphVisible ? new[] { 0, 0, 255 } : new[] { 255, 255, 255 }), "the glyph");
      Assert.That(Pixel(80, 25), Is.EqualTo(new[] { 0, 255, 0 }), "the fill after ET, outside every glyph: not clipped away");
    });
  }

  [Test]
  [Category("Unit")]
  public void UnknownCMapName_IsRefusedByName() {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /J 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("BT /J 20 Tf 5 5 Td <3042> Tj ET"),
      "<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /NoSuch-UCS2-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>] >>");
    var failure = Assert.Throws<NotSupportedException>(() => PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0));
    Assert.That(failure!.Message, Does.Contain("NoSuch-UCS2-H"));
  }

  /// <remarks>
  /// poppler does not implement knockout groups — it composites their elements over one another —
  /// so this is held to the specification's arithmetic (§11.4.6.4) instead: inside an isolated
  /// knockout group, the second of two half-transparent squares replaces the first where they
  /// overlap rather than lying over it.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void KnockoutGroup_LetsTheLaterElementReplaceTheEarlier() {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 30 10] /Resources << /XObject << /G 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("/G Do"),
      "<< /Type /XObject /Subtype /Form /BBox [0 0 30 10] /Group << /S /Transparency /I true /K true >> /Resources << /ExtGState << /H << /ca 0.5 >> >> >> /Length 44 >>\nstream\n/H gs 1 0 0 rg 0 0 20 10 re f 0 0 1 rg 10 0 20 10 re f\nendstream");
    var page = PdfFile.ToRawImage(PdfReader.FromBytes(pdf), 0);
    byte[] At(int x) => page.PixelData.AsSpan((5 * page.Width + x) * 3, 3).ToArray();
    Assert.Multiple(() => {
      Assert.That(At(5), Is.EqualTo(new byte[] { 255, 128, 128 }), "red alone");
      Assert.That(At(15), Is.EqualTo(new byte[] { 128, 128, 255 }), "blue knocks the red out");
      Assert.That(At(25), Is.EqualTo(new byte[] { 128, 128, 255 }), "blue alone");
    });
  }

  [Test]
  [Category("Unit")]
  public void DeviceCmyk_MatchesPopplersConversion() {
    // pdftoppm's own output for these four colours.
    var pdf = _OnePage("[0 0 4 1]", "1 0 0 0 k 0 0 1 1 re f 0 1 0 0 k 1 0 1 1 re f 0 0 0 1 k 2 0 1 1 re f 1 1 1 0 k 3 0 1 1 re f");
    var page = PdfFile.ToRawImage(PdfReader.FromBytes(pdf), 0);
    Assert.That(page.PixelData, Is.EqualTo(new byte[] { 0, 173, 239, 236, 0, 140, 35, 31, 32, 54, 54, 57 }));
  }

  [Test]
  [Category("Unit")]
  public void PostScriptCalculator_EvaluatesItsProgram() {
    var document = PdfDocument.Open(_OnePage("[0 0 1 1]", ""));
    var stream = new PdfStreamObject(_Dict("/FunctionType 4 /Domain [0 1 0 1] /Range [0 10 0 10 0 10]"),
      Encoding.ASCII.GetBytes("{ 2 copy mul 3 1 roll exch dup 0.5 gt { 1 exch sub } { 2 mul } ifelse }"));
    var function = PdfFunction.Parse(document, stream);
    Span<double> output = stackalloc double[3];
    function.Evaluate([0.2, 0.7], output);
    Assert.That(output.ToArray(), Is.EqualTo(new[] { 0.14, 0.7, 0.4 }).Within(1e-9));
    function.Evaluate([0.9, 0.3], output);
    Assert.That(output.ToArray(), Is.EqualTo(new[] { 0.27, 0.3, 0.1 }).Within(1e-9));
  }

  [Test]
  [Category("Unit")]
  public void SampledAndStitchingFunctions_Interpolate() {
    var document = PdfDocument.Open(_OnePage("[0 0 1 1]", ""));
    var sampled = PdfFunction.Parse(document, new PdfStreamObject(_Dict("/FunctionType 0 /Domain [0 1] /Range [0 1] /Size [3] /BitsPerSample 8"), [0, 255, 51]));
    var stitched = PdfFunction.Parse(document, _Dict("/FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 1 0] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 2 >>]"));
    Span<double> one = stackalloc double[1];
    sampled.Evaluate(0.25, one);
    Assert.That(one[0], Is.EqualTo(0.5).Within(1e-9));
    sampled.Evaluate(0.75, one);
    Assert.That(one[0], Is.EqualTo(0.6).Within(1e-9));
    stitched.Evaluate(0.25, one);
    Assert.That(one[0], Is.EqualTo(0.5).Within(1e-9));
    stitched.Evaluate(0.75, one);
    Assert.That(one[0], Is.EqualTo(0.25).Within(1e-9));
  }

  /// <summary>
  /// Renders every page of every PDF in the folder named by <c>PDF_CORPUS</c> and compares it with
  /// poppler's pdftoppm at the same resolution, over the crop box.
  /// </summary>
  /// <remarks>
  /// <c>PDF_CORPUS_PAGES</c> limits the pages per file (default 3), <c>PDF_CORPUS_DPI</c> sets the
  /// resolution (default 72). A page passes when its mean absolute error is at most 6 and at most 5%
  /// of its pixels are off by more than 48 in some channel — loose enough for antialiasing and
  /// resampling to differ, tight enough that a missing or misplaced object fails. A page that is
  /// refused by name (<see cref="NotSupportedException"/>) is reported but does not fail the run; any
  /// other exception does.
  /// </remarks>
  [Test]
  [Explicit("Needs a folder of PDFs in PDF_CORPUS and poppler's pdftoppm.")]
  [Category("Regression")]
  public void Corpus_AgreesWithPoppler() {
    var folder = Environment.GetEnvironmentVariable("PDF_CORPUS");
    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
      Assert.Ignore("Set PDF_CORPUS to a folder of PDFs.");

    var pages = int.TryParse(Environment.GetEnvironmentVariable("PDF_CORPUS_PAGES"), out var p) ? p : 3;
    var resolution = double.TryParse(Environment.GetEnvironmentVariable("PDF_CORPUS_DPI"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 72;
    var report = new StringBuilder();
    var failures = 0;
    var refused = 0;
    var passed = 0;
    foreach (var path in Directory.GetFiles(folder!, "*.pdf")) {
      var name = Path.GetFileName(path);
      PdfFile file;
      try {
        file = PdfReader.FromBytes(File.ReadAllBytes(path));
        _ = PdfFile.ImageCount(file);
      } catch (NotSupportedException e) {
        report.AppendLine($"REFUSED {name}: {e.Message}");
        ++refused;
        continue;
      } catch (Exception e) {
        report.AppendLine($"FAIL    {name}: {e.GetType().Name}: {e.Message}");
        ++failures;
        continue;
      }

      for (var i = 0; i < Math.Min(pages, file.Pages.Count); ++i) {
        PdfPageRendering rendering;
        try {
          rendering = PdfFile.RenderPage(file, i, new PdfRenderOptions { Resolution = resolution });
        } catch (NotSupportedException e) {
          report.AppendLine($"REFUSED {name} p{i + 1}: {e.Message}");
          ++refused;
          continue;
        } catch (Exception e) {
          report.AppendLine($"FAIL    {name} p{i + 1}: {e.GetType().Name}: {e.Message}");
          ++failures;
          continue;
        }

        var reference = PdfRenderComparison.Poppler(path, i, resolution * file.Pages[i].UserUnit);
        if (reference == null) {
          report.AppendLine($"SKIP    {name} p{i + 1}: pdftoppm produced nothing");
          continue;
        }

        var comparison = PdfRenderComparison.Of(rendering.Image, reference);
        var ok = comparison.SameSize && comparison.MeanAbsoluteError <= 6 && comparison.BadPercent <= 5;
        var omitted = rendering.Omissions.Count > 0 ? " (omitted: " + string.Join("; ", new HashSet<string>(rendering.Omissions)) + ")" : string.Empty;
        report.AppendLine($"{(ok ? "PASS" : "FAIL"),-7} {name} p{i + 1}: {comparison}{omitted}");
        if (ok)
          ++passed;
        else
          ++failures;
      }
    }

    TestContext.Out.WriteLine(report.ToString());
    TestContext.Out.WriteLine($"{passed} passed, {failures} failed, {refused} refused");
    Assert.That(failures, Is.Zero, report.ToString());
  }

  private static PdfDict _Dict(string body) => (PdfDict)new PdfLexer(Encoding.Latin1.GetBytes("<< " + body + " >>")).ReadObject()!;

  private static string _Stream(string content) => $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";

  private static byte[] _OnePage(string mediaBox, string content) => _Build(
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /Contents 4 0 R >>",
    _Stream(content));

  /// <summary>A classic-xref PDF from object bodies numbered from 1.</summary>
  internal static byte[] _Build(params string[] objects) {
    var text = new StringBuilder("%PDF-1.7\n");
    var offsets = new List<int>();
    for (var i = 0; i < objects.Length; ++i) {
      offsets.Add(Encoding.Latin1.GetByteCount(text.ToString()));
      text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
    }

    var xref = Encoding.Latin1.GetByteCount(text.ToString());
    text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
    foreach (var offset in offsets)
      text.Append($"{offset:D10} 00000 n \n");
    text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return Encoding.Latin1.GetBytes(text.ToString());
  }
}
