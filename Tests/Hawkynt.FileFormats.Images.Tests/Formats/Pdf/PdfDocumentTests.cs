using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Tests;

/// <summary>
/// The document layer: cross-reference forms, object streams, incremental updates, recovery,
/// encryption and the page tree.
/// </summary>
/// <remarks>
/// The fixtures are one small drawing written by hand and then rewritten by qpdf in each of the
/// forms a reader has to cope with — object streams, linearised, RC4 at 40 and 128 bits, AES at 128
/// and 256 bits (revisions 4, 5 and 6) — plus an appended incremental update and a copy whose
/// cross-reference points nowhere. Every one of them must yield the same content stream.
/// </remarks>
[TestFixture]
public sealed class PdfDocumentTests {

  private const string _ShapesContent = "1 0 0 rg 10 10 80 40 re f 0 0 1 RG 4 w 100 20 m 190 80 l S 0 g 120 60 m 150 95 l 180 60 l h f";

  internal static byte[] Fixture(string relative) {
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdf", relative);
    return File.ReadAllBytes(path);
  }

  private static string _ContentOf(PdfDocument document, int page) {
    var contents = document.Resolve(document.Pages[page].Dictionary.Raw("Contents"));
    var text = new StringBuilder();
    if (contents is PdfStreamObject single)
      text.Append(Encoding.Latin1.GetString(document.Decode(single)));
    else if (contents is List<object?> parts)
      foreach (var part in parts)
        if (document.Stream(part) is { } stream)
          text.Append(Encoding.Latin1.GetString(document.Decode(stream)));

    return text.ToString();
  }

  [TestCase("shapes.pdf", false)]
  [TestCase("shapes-objstm.pdf", false)]
  [TestCase("brokenxref.pdf", false)]
  [TestCase("shapes-rc4-40.pdf", true)]
  [TestCase("shapes-rc4-128.pdf", true)]
  [TestCase("shapes-aes128.pdf", true)]
  [TestCase("shapes-aes256r5.pdf", true)]
  [TestCase("shapes-aes256.pdf", true)]
  [Category("Unit")]
  public void EveryFormOfTheSameFile_YieldsTheSameContent(string name, bool encrypted) {
    var document = PdfDocument.Open(Fixture("Structure/" + name));
    Assert.Multiple(() => {
      Assert.That(document.IsEncrypted, Is.EqualTo(encrypted));
      Assert.That(document.Pages, Has.Count.EqualTo(1));
      Assert.That(_ContentOf(document, 0), Is.EqualTo(_ShapesContent));
    });
  }

  [Test]
  [Category("Unit")]
  public void IncrementalUpdate_ReplacesTheObjectItRewrites() {
    var document = PdfDocument.Open(Fixture("Structure/incremental.pdf"));
    Assert.That(_ContentOf(document, 0), Is.EqualTo("0 1 0 rg 0 0 200 100 re f"));
  }

  [Test]
  [Category("Unit")]
  public void UserPassword_IsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => PdfDocument.Open(Fixture("Structure/shapes-userpw.pdf")));
    Assert.That(failure!.Message, Does.Contain("password"));
  }

  [TestCase("tree.pdf")]
  [TestCase("tree-aes128-objstm.pdf")]
  [TestCase("tree-aes256-objstm.pdf")]
  [Category("Unit")]
  public void PageTree_InheritsBoxesAndRotation_AndJoinsContentArrays(string name) {
    var document = PdfDocument.Open(Fixture("Structure/" + name));
    Assert.That(document.Pages, Has.Count.EqualTo(3));
    Assert.Multiple(() => {
      Assert.That(document.Pages[0].MediaBox, Is.EqualTo(new PdfBox(0, 0, 120, 80)));
      Assert.That(document.Pages[0].Rotate, Is.EqualTo(180));
      Assert.That(_ContentOf(document, 0), Is.EqualTo("0 1 0 rg 10 10 50 30 re f"));
      Assert.That(document.Pages[1].MediaBox, Is.EqualTo(new PdfBox(0, 0, 60, 90)));
      Assert.That(document.Pages[1].Rotate, Is.EqualTo(0), "the page's own /Rotate 0 overrides the inherited 180");
      Assert.That(document.Pages[2].MediaBox, Is.EqualTo(new PdfBox(0, 0, 90, 60)));
      Assert.That(document.Pages[2].Rotate, Is.EqualTo(180));
    });
  }

  [Test]
  [Category("Unit")]
  public void Pages_ReportCropRotationAndUserUnit() {
    var cropped = PdfReader.FromBytes(Fixture("Structure/rot270crop.pdf")).Pages[0];
    var scaled = PdfReader.FromBytes(Fixture("Structure/userunit.pdf")).Pages[0];
    Assert.Multiple(() => {
      Assert.That(cropped.CropBox, Is.EqualTo(new PdfBox(5, 5, 150, 90)));
      Assert.That(cropped.Rotation, Is.EqualTo(270));
      Assert.That(cropped.Width, Is.EqualTo(85));
      Assert.That(cropped.Height, Is.EqualTo(145));
      Assert.That(scaled.UserUnit, Is.EqualTo(2));
      Assert.That(scaled.Width, Is.EqualTo(400));
      Assert.That(scaled.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Rc4_MatchesTheRfc6229TestVector() {
    // RFC 6229, key 0x0102030405 (40 bits), keystream at offset 0.
    var keystream = PdfSecurityHandler.Rc4([1, 2, 3, 4, 5], new byte[16]);
    Assert.That(Convert.ToHexString(keystream), Is.EqualTo("B2396305F03DC027CCC3524A0A1118A8"));
  }

  [Test]
  [Category("Unit")]
  public void Filters_DecodeTheirOwnTextbookExamples() {
    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(PdfFilters.Ascii85(Encoding.ASCII.GetBytes("87cURD]i,\"Ebo80~>"))), Is.EqualTo("Hello World!"));
      Assert.That(PdfFilters.Ascii85(Encoding.ASCII.GetBytes("z~>")), Is.EqualTo(new byte[4]));
      Assert.That(PdfFilters.AsciiHex(Encoding.ASCII.GetBytes("48 65 6c6C 7>")), Is.EqualTo(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x70 }));
      Assert.That(PdfFilters.RunLength([2, 1, 2, 3, 254, 9, 128]), Is.EqualTo(new byte[] { 1, 2, 3, 9, 9, 9 }));
      // The LZW example of PDF 32000-1 §7.4.4.2: 45 45 45 45 45 65 45 45 45 66.
      Assert.That(PdfFilters.Lzw([0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01], true), Is.EqualTo(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Lexer_KeepsNamesStringsNumbersAndReferencesApart() {
    var lexer = new PdfLexer(Encoding.Latin1.GetBytes("<< /A#20B (x\\)y\\101) /N -.5 /R 12 0 R /H <414> /K [1 2.0 true null] >>"));
    var dict = (PdfDict)lexer.ReadObject()!;
    Assert.Multiple(() => {
      Assert.That(dict["A B"], Is.EqualTo(Encoding.Latin1.GetBytes("x)yA")));
      Assert.That(dict["N"], Is.EqualTo(-0.5));
      Assert.That(dict["R"], Is.EqualTo(new PdfRef(12, 0)));
      Assert.That(dict["H"], Is.EqualTo(new byte[] { 0x41, 0x40 }));
      Assert.That(dict["K"], Is.EqualTo(new List<object?> { 1, 2.0, true, null }));
    });
  }
}
