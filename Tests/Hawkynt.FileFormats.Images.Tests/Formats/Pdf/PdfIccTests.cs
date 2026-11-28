using System.IO;
using FileFormat.Pdf.Rendering.Icc;

namespace FileFormat.Pdf.Tests;

/// <summary>ICC profile evaluation, held to Little CMS on the self-made profiles in Fixtures/Pdf/Color.</summary>
/// <remarks>
/// The expected values were captured from the system's Little CMS 2 through its C API — an 8-bit
/// transform to cmsCreate_sRGBProfile with cmsFLAGS_NOOPTIMIZE and, except for absolute colorimetric,
/// cmsFLAGS_BLACKPOINTCOMPENSATION, which is the transform poppler builds; and cmsDetectBlackPoint.
/// The profiles, made by Fixtures/make_fixtures.py, cover a v2 grey curv table, a v4 matrix-shaper
/// with three kinds of para curve, a v4 RGB lutAtoB with every element, CMYK lut16 and lut8 tables
/// with a Lab PCS, and a v4 CMYK lutAtoB / lutBtoA pair.
/// </remarks>
[TestFixture]
public sealed class PdfIccTests {

  private static IccProfile _Profile(string name) => IccProfile.TryParse(PdfDocumentTests.Fixture(Path.Combine("Color", name + ".icc")))!;

  [TestCase("Gray", 0, new byte[] { 0 }, 0, 0, 0)]
  [TestCase("Gray", 0, new byte[] { 77 }, 96, 96, 96)]
  [TestCase("Gray", 0, new byte[] { 200 }, 210, 210, 210)]
  [TestCase("Gray", 0, new byte[] { 255 }, 255, 255, 255)]
  [TestCase("Gray", 1, new byte[] { 0 }, 0, 0, 0)]
  [TestCase("Gray", 1, new byte[] { 77 }, 96, 96, 96)]
  [TestCase("Gray", 1, new byte[] { 200 }, 210, 210, 210)]
  [TestCase("Gray", 1, new byte[] { 255 }, 255, 255, 255)]
  [TestCase("Gray", 2, new byte[] { 0 }, 0, 0, 0)]
  [TestCase("Gray", 2, new byte[] { 77 }, 96, 96, 96)]
  [TestCase("Gray", 2, new byte[] { 200 }, 210, 210, 210)]
  [TestCase("Gray", 2, new byte[] { 255 }, 255, 255, 255)]
  [TestCase("Gray", 3, new byte[] { 0 }, 0, 0, 0)]
  [TestCase("Gray", 3, new byte[] { 77 }, 96, 96, 96)]
  [TestCase("Gray", 3, new byte[] { 200 }, 210, 210, 210)]
  [TestCase("Gray", 3, new byte[] { 255 }, 255, 255, 255)]
  [TestCase("RgbTrc", 0, new byte[] { 0, 0, 0 }, 0, 0, 0)]
  [TestCase("RgbTrc", 0, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbTrc", 0, new byte[] { 200, 40, 90 }, 230, 53, 108)]
  [TestCase("RgbTrc", 0, new byte[] { 13, 180, 250 }, 0, 193, 255)]
  [TestCase("RgbTrc", 1, new byte[] { 0, 0, 0 }, 0, 0, 0)]
  [TestCase("RgbTrc", 1, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbTrc", 1, new byte[] { 200, 40, 90 }, 230, 53, 108)]
  [TestCase("RgbTrc", 1, new byte[] { 13, 180, 250 }, 0, 193, 255)]
  [TestCase("RgbTrc", 2, new byte[] { 0, 0, 0 }, 0, 0, 0)]
  [TestCase("RgbTrc", 2, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbTrc", 2, new byte[] { 200, 40, 90 }, 230, 53, 108)]
  [TestCase("RgbTrc", 2, new byte[] { 13, 180, 250 }, 0, 193, 255)]
  [TestCase("RgbTrc", 3, new byte[] { 0, 0, 0 }, 0, 0, 0)]
  [TestCase("RgbTrc", 3, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbTrc", 3, new byte[] { 200, 40, 90 }, 230, 53, 108)]
  [TestCase("RgbTrc", 3, new byte[] { 13, 180, 250 }, 0, 193, 255)]
  [TestCase("RgbLut", 0, new byte[] { 0, 0, 0 }, 26, 0, 0)]
  [TestCase("RgbLut", 0, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbLut", 0, new byte[] { 200, 40, 90 }, 255, 69, 231)]
  [TestCase("RgbLut", 0, new byte[] { 13, 180, 250 }, 58, 255, 255)]
  [TestCase("RgbLut", 1, new byte[] { 0, 0, 0 }, 31, 0, 0)]
  [TestCase("RgbLut", 1, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbLut", 1, new byte[] { 200, 40, 90 }, 255, 71, 231)]
  [TestCase("RgbLut", 1, new byte[] { 13, 180, 250 }, 60, 255, 255)]
  [TestCase("RgbLut", 2, new byte[] { 0, 0, 0 }, 26, 0, 0)]
  [TestCase("RgbLut", 2, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbLut", 2, new byte[] { 200, 40, 90 }, 255, 69, 231)]
  [TestCase("RgbLut", 2, new byte[] { 13, 180, 250 }, 58, 255, 255)]
  [TestCase("RgbLut", 3, new byte[] { 0, 0, 0 }, 31, 0, 0)]
  [TestCase("RgbLut", 3, new byte[] { 255, 255, 255 }, 255, 255, 255)]
  [TestCase("RgbLut", 3, new byte[] { 200, 40, 90 }, 255, 71, 231)]
  [TestCase("RgbLut", 3, new byte[] { 13, 180, 250 }, 60, 255, 255)]
  [TestCase("CmykV2", 0, new byte[] { 0, 0, 0, 0 }, 247, 247, 248)]
  [TestCase("CmykV2", 0, new byte[] { 255, 255, 255, 255 }, 0, 0, 0)]
  [TestCase("CmykV2", 0, new byte[] { 30, 200, 10, 60 }, 172, 64, 183)]
  [TestCase("CmykV2", 0, new byte[] { 128, 64, 220, 0 }, 139, 183, 58)]
  [TestCase("CmykV2", 1, new byte[] { 0, 0, 0, 0 }, 244, 246, 247)]
  [TestCase("CmykV2", 1, new byte[] { 255, 255, 255, 255 }, 0, 0, 1)]
  [TestCase("CmykV2", 1, new byte[] { 30, 200, 10, 60 }, 169, 64, 183)]
  [TestCase("CmykV2", 1, new byte[] { 128, 64, 220, 0 }, 136, 182, 59)]
  [TestCase("CmykV2", 2, new byte[] { 0, 0, 0, 0 }, 248, 248, 248)]
  [TestCase("CmykV2", 2, new byte[] { 255, 255, 255, 255 }, 33, 33, 33)]
  [TestCase("CmykV2", 2, new byte[] { 30, 200, 10, 60 }, 173, 72, 185)]
  [TestCase("CmykV2", 2, new byte[] { 128, 64, 220, 0 }, 141, 185, 67)]
  [TestCase("CmykV2", 3, new byte[] { 0, 0, 0, 0 }, 239, 244, 244)]
  [TestCase("CmykV2", 3, new byte[] { 255, 255, 255, 255 }, 31, 33, 33)]
  [TestCase("CmykV2", 3, new byte[] { 30, 200, 10, 60 }, 168, 73, 182)]
  [TestCase("CmykV2", 3, new byte[] { 128, 64, 220, 0 }, 136, 182, 67)]
  [TestCase("CmykV4", 0, new byte[] { 0, 0, 0, 0 }, 248, 248, 248)]
  [TestCase("CmykV4", 0, new byte[] { 255, 255, 255, 255 }, 28, 29, 29)]
  [TestCase("CmykV4", 0, new byte[] { 30, 200, 10, 60 }, 182, 75, 192)]
  [TestCase("CmykV4", 0, new byte[] { 128, 64, 220, 0 }, 149, 191, 68)]
  [TestCase("CmykV4", 1, new byte[] { 0, 0, 0, 0 }, 247, 247, 247)]
  [TestCase("CmykV4", 1, new byte[] { 255, 255, 255, 255 }, 0, 0, 0)]
  [TestCase("CmykV4", 1, new byte[] { 30, 200, 10, 60 }, 180, 63, 190)]
  [TestCase("CmykV4", 1, new byte[] { 128, 64, 220, 0 }, 145, 190, 53)]
  [TestCase("CmykV4", 2, new byte[] { 0, 0, 0, 0 }, 248, 248, 248)]
  [TestCase("CmykV4", 2, new byte[] { 255, 255, 255, 255 }, 28, 29, 29)]
  [TestCase("CmykV4", 2, new byte[] { 30, 200, 10, 60 }, 182, 75, 192)]
  [TestCase("CmykV4", 2, new byte[] { 128, 64, 220, 0 }, 149, 191, 68)]
  [TestCase("CmykV4", 3, new byte[] { 0, 0, 0, 0 }, 242, 245, 244)]
  [TestCase("CmykV4", 3, new byte[] { 255, 255, 255, 255 }, 32, 33, 33)]
  [TestCase("CmykV4", 3, new byte[] { 30, 200, 10, 60 }, 179, 77, 190)]
  [TestCase("CmykV4", 3, new byte[] { 128, 64, 220, 0 }, 146, 189, 69)]
  [TestCase("Cmyk8", 0, new byte[] { 0, 0, 0, 0 }, 247, 247, 247)]
  [TestCase("Cmyk8", 0, new byte[] { 255, 255, 255, 255 }, 0, 0, 0)]
  [TestCase("Cmyk8", 0, new byte[] { 30, 200, 10, 60 }, 171, 64, 184)]
  [TestCase("Cmyk8", 0, new byte[] { 128, 64, 220, 0 }, 138, 183, 57)]
  [TestCase("Cmyk8", 1, new byte[] { 0, 0, 0, 0 }, 247, 247, 247)]
  [TestCase("Cmyk8", 1, new byte[] { 255, 255, 255, 255 }, 33, 33, 33)]
  [TestCase("Cmyk8", 1, new byte[] { 30, 200, 10, 60 }, 173, 73, 185)]
  [TestCase("Cmyk8", 1, new byte[] { 128, 64, 220, 0 }, 141, 184, 67)]
  [TestCase("Cmyk8", 2, new byte[] { 0, 0, 0, 0 }, 247, 247, 247)]
  [TestCase("Cmyk8", 2, new byte[] { 255, 255, 255, 255 }, 33, 33, 33)]
  [TestCase("Cmyk8", 2, new byte[] { 30, 200, 10, 60 }, 173, 73, 185)]
  [TestCase("Cmyk8", 2, new byte[] { 128, 64, 220, 0 }, 141, 184, 67)]
  [TestCase("Cmyk8", 3, new byte[] { 0, 0, 0, 0 }, 247, 247, 247)]
  [TestCase("Cmyk8", 3, new byte[] { 255, 255, 255, 255 }, 33, 33, 33)]
  [TestCase("Cmyk8", 3, new byte[] { 30, 200, 10, 60 }, 173, 73, 185)]
  [TestCase("Cmyk8", 3, new byte[] { 128, 64, 220, 0 }, 141, 184, 67)]
  [Category("Unit")]
  public void Transform_AgreesWithLittleCms(string profile, int intent, byte[] input, int r, int g, int b) {
    var transform = IccTransform.Create(_Profile(profile), (IccIntent)intent);
    Assert.That(transform, Is.Not.Null);
    var rgb = new byte[3];
    transform!.Convert(input, rgb);
    Assert.That(rgb, Is.EqualTo(new[] { (byte)r, (byte)g, (byte)b }));
  }

  [TestCase("Cmyk8", 0, 0.014974134716563123, 0.015530112753124999, 0.01281079001005281)]
  [TestCase("Cmyk8", 1, 0.0, 0.0, 0.0)]
  [TestCase("Cmyk8", 2, 0.0, 0.0, 0.0)]
  [TestCase("CmykV2", 0, 0.014843738353490168, 0.015394874873978602, 0.01269923228354495)]
  [TestCase("CmykV2", 1, 0.015009828784311072, 0.015567132113991987, 0.01284132728083199)]
  [TestCase("CmykV2", 2, 0.0, 0.0, 0.0)]
  [TestCase("CmykV4", 0, 0.00336, 0.0034731, 0.00287)]
  [TestCase("CmykV4", 1, 0.024867796373998573, 0.025791118413190806, 0.021275093579041095)]
  [TestCase("CmykV4", 2, 0.00336, 0.0034731, 0.00287)]
  [TestCase("Gray", 0, 0.0, 0.0, 0.0)]
  [TestCase("Gray", 1, 0.0, 0.0, 0.0)]
  [TestCase("Gray", 2, 0.0, 0.0, 0.0)]
  [TestCase("RgbLut", 0, 0.00336, 0.0034731, 0.00287)]
  [TestCase("RgbLut", 1, 0.0, 0.0, 0.0)]
  [TestCase("RgbLut", 2, 0.00336, 0.0034731, 0.00287)]
  [TestCase("RgbTrc", 0, 0.0, 0.0, 0.0)]
  [TestCase("RgbTrc", 1, 0.0, 0.0, 0.0)]
  [TestCase("RgbTrc", 2, 0.0, 0.0, 0.0)]
  [Category("Unit")]
  public void DetectBlackPoint_AgreesWithLittleCms(string profile, int intent, double x, double y, double z) {
    var black = IccTransform.DetectBlackPoint(_Profile(profile), (IccIntent)intent);
    Assert.Multiple(() => {
      Assert.That(black.X, Is.EqualTo(x).Within(1e-9));
      Assert.That(black.Y, Is.EqualTo(y).Within(1e-9));
      Assert.That(black.Z, Is.EqualTo(z).Within(1e-9));
    });
  }

  [Test]
  [Category("Unit")]
  public void TryParse_RejectsWhatIsNotAProfile() {
    Assert.That(IccProfile.TryParse(new byte[200]), Is.Null);
    Assert.That(IccProfile.TryParse([1, 2, 3]), Is.Null);
  }
}
