using System.Linq;
using FileFormat.Pdf.Fonts.Data;
using FileFormat.Pdf.Rendering;

namespace FileFormat.Pdf.Tests;

/// <summary>The bundled predefined CMaps, held to Adobe's cmap-resources text they were packed from.</summary>
/// <remarks>
/// Each expectation was read off the CMap file in adobe-type-tools/cmap-resources by hand (the
/// range holding the code, then its offset); the render fixture Render/cmaps.pdf checks the same
/// CMaps against poppler, which reads them from poppler-data.
/// </remarks>
[TestFixture]
public sealed class PdfCMapTests {

  // 90ms-RKSJ-H: <8140> <817e> 633, <889f> <88fc> 1125, and notdefrange <00> <1f> 231.
  [TestCase("90ms-RKSJ-H", new byte[] { 0x81, 0x40 }, 633)]
  [TestCase("90ms-RKSJ-H", new byte[] { 0x88, 0xA0 }, 1126)]
  [TestCase("90ms-RKSJ-H", new byte[] { 0x05 }, 231)]
  // UniJIS-UCS2-H: <0020> <005b> 1, <005c> 97, <3000> <3002> 633; UniJIS-UCS2-V: <3001> <3002> 7887 over it.
  [TestCase("UniJIS-UCS2-H", new byte[] { 0x00, 0x41 }, 34)]
  [TestCase("UniJIS-UCS2-H", new byte[] { 0x00, 0x5C }, 97)]
  [TestCase("UniJIS-UCS2-H", new byte[] { 0x30, 0x01 }, 634)]
  [TestCase("UniJIS-UCS2-V", new byte[] { 0x30, 0x01 }, 7887)]
  [TestCase("UniJIS-UCS2-V", new byte[] { 0x00, 0x41 }, 34)]
  // UniJIS-UTF16-H: <d840dc0b> 13839, a four-byte surrogate pair.
  [TestCase("UniJIS-UTF16-H", new byte[] { 0xD8, 0x40, 0xDC, 0x0B }, 13839)]
  // GBK-EUC-H <b0a1> <b0fe> 940, KSC-EUC-H <b0a1> <b0fe> 1086, ETen-B5-H <a440> <a47e> 595.
  [TestCase("GBK-EUC-H", new byte[] { 0xB0, 0xA2 }, 941)]
  [TestCase("KSC-EUC-H", new byte[] { 0xB0, 0xA1 }, 1086)]
  [TestCase("ETen-B5-H", new byte[] { 0xA4, 0x41 }, 596)]
  [Category("Unit")]
  public void Predefined_MapsLikeAdobesFile(string name, byte[] text, int cid) {
    var map = PdfCMap.Predefined(name);
    Assert.That(map, Is.Not.Null);
    var codes = map!.Codes(text).ToList();
    Assert.That(codes, Has.Count.EqualTo(1), "one code");
    Assert.That(map.Cid(codes[0].Code, codes[0].Length), Is.EqualTo(cid));
  }

  [Test]
  [Category("Unit")]
  public void Predefined_SplitsMixedLengthsByCodespace() {
    // 90ms-RKSJ-H: one byte for ASCII and half-width katakana, two for the rest.
    var map = PdfCMap.Predefined("90ms-RKSJ-H")!;
    var codes = map.Codes([0x41, 0x82, 0xA0, 0xB1, 0x88, 0x9F]).ToList();
    Assert.That(codes.Select(c => c.Length), Is.EqualTo(new[] { 1, 2, 1, 2 }));
  }

  [Test]
  [Category("Unit")]
  public void Predefined_KnowsWritingModeAndEveryTable118Name() {
    Assert.Multiple(() => {
      Assert.That(PdfCMap.Predefined("UniJIS-UCS2-V")!.Vertical, Is.True);
      Assert.That(PdfCMap.Predefined("UniJIS-UCS2-H")!.Vertical, Is.False);
      Assert.That(PdfCMap.Predefined("NoSuch-H"), Is.Null);
      string[] table118 = [
        "GB-EUC-H", "GB-EUC-V", "GBpc-EUC-H", "GBpc-EUC-V", "GBK-EUC-H", "GBK-EUC-V", "GBKp-EUC-H", "GBKp-EUC-V", "GBK2K-H", "GBK2K-V",
        "UniGB-UCS2-H", "UniGB-UCS2-V", "UniGB-UTF16-H", "UniGB-UTF16-V", "B5pc-H", "B5pc-V", "HKscs-B5-H", "HKscs-B5-V", "ETen-B5-H",
        "ETen-B5-V", "ETenms-B5-H", "ETenms-B5-V", "CNS-EUC-H", "CNS-EUC-V", "UniCNS-UCS2-H", "UniCNS-UCS2-V", "UniCNS-UTF16-H",
        "UniCNS-UTF16-V", "83pv-RKSJ-H", "90ms-RKSJ-H", "90ms-RKSJ-V", "90msp-RKSJ-H", "90msp-RKSJ-V", "90pv-RKSJ-H", "Add-RKSJ-H",
        "Add-RKSJ-V", "EUC-H", "EUC-V", "Ext-RKSJ-H", "Ext-RKSJ-V", "H", "V", "UniJIS-UCS2-H", "UniJIS-UCS2-V", "UniJIS-UCS2-HW-H",
        "UniJIS-UCS2-HW-V", "UniJIS-UTF16-H", "UniJIS-UTF16-V", "KSC-EUC-H", "KSC-EUC-V", "KSCms-UHC-H", "KSCms-UHC-V",
        "KSCms-UHC-HW-H", "KSCms-UHC-HW-V", "KSCpc-EUC-H", "UniKS-UCS2-H", "UniKS-UCS2-V", "UniKS-UTF16-H", "UniKS-UTF16-V",
      ];
      foreach (var name in table118)
        Assert.That(PdfPredefinedCMaps.Find(name), Is.Not.Null, name);
    });
  }
}
