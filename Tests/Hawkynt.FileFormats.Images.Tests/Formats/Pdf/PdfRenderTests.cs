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
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /S << /Type /Font /Subtype /Type1 /BaseFont /Symbol >> /H << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents 4 0 R >>",
      _Stream("BT /S 20 Tf 5 5 Td (abg) Tj /H 20 Tf (Hi) Tj ET"));
    var rendering = PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0);
    Assert.Multiple(() => {
      Assert.That(rendering.Omissions, Has.Count.EqualTo(1));
      Assert.That(rendering.Omissions[0], Does.Contain("Symbol"));
      Assert.That(rendering.Image.PixelData, Has.Some.LessThan(128), "the Helvetica text after it is still drawn");
    });
  }

  [Test]
  [Category("Unit")]
  public void PredefinedCjkCMap_IsRefusedByName() {
    var pdf = _Build(
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 30] /Resources << /Font << /J 5 0 R >> >> /Contents 4 0 R >>",
      _Stream("BT /J 20 Tf 5 5 Td <3042> Tj ET"),
      "<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /UniJIS-UCS2-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>] >>");
    var failure = Assert.Throws<NotSupportedException>(() => PdfFile.RenderPage(PdfReader.FromBytes(pdf), 0));
    Assert.That(failure!.Message, Does.Contain("UniJIS-UCS2-H"));
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
