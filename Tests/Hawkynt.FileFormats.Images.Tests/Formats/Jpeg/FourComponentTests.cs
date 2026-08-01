using FileFormat.Core;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// Four-component Adobe pictures, where the planes are ink rather than colour.
/// </summary>
/// <remarks>
/// These used to be read by taking the first three planes as luma and chroma and dropping the
/// fourth, which loses the black plate and leaves every dark area pale — a picture came out about
/// 190 of 255 away from what any other decoder makes of it.
/// <para/>
/// The subtlety that makes them easy to get wrong is that Adobe stores ink inverted, and the two
/// transforms differ in how far that survives: undoing a YCCK file's luma-chroma step gives ink the
/// right way up again while its key plane stays inverted, whereas an untransformed file has all
/// four inverted. Treating both alike leaves one a negative of itself.
/// </remarks>
[TestFixture]
public sealed class FourComponentTests {

  [Test]
  [Category("Unit")]
  public void Ycck_TurnsRecoveredInkBackIntoLight() {
    // Red on paper is cyan ink and nothing else, so the three planes carry the luma and chroma of
    // cyan; a stored key of 255 is no black at all. What comes out must be red.
    var c = new byte[] { 179 };
    var m = new byte[] { 171 };
    var y = new byte[] { 0 };
    var k = new byte[] { 255 };

    var rgb = JpegColorConverter.YcckOrCmykToRgb(c, m, y, k, 1, 1, JpegColorConverter.AdobeTransformYcck);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255).Within(2), "red");
      Assert.That(rgb[1], Is.EqualTo(0).Within(2));
      Assert.That(rgb[2], Is.EqualTo(0).Within(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Cmyk_TakesItsStoredPlanesAsLightAlready() {
    // Untransformed Adobe planes are inverted throughout, so a stored 255 is no ink.
    var rgb = JpegColorConverter.YcckOrCmykToRgb(
      [255], [0], [0], [255], 1, 1, JpegColorConverter.AdobeTransformNone);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255));
      Assert.That(rgb[1], Is.EqualTo(0));
      Assert.That(rgb[2], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Key_DarkensWhatTheOtherPlanesLeave() {
    // Half the key means half the light, whichever transform the file states.
    var full = JpegColorConverter.YcckOrCmykToRgb(
      [255], [255], [255], [255], 1, 1, JpegColorConverter.AdobeTransformNone);
    var half = JpegColorConverter.YcckOrCmykToRgb(
      [255], [255], [255], [128], 1, 1, JpegColorConverter.AdobeTransformNone);

    Assert.Multiple(() => {
      Assert.That(full[0], Is.EqualTo(255));
      Assert.That(half[0], Is.EqualTo(128).Within(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void Key_OfZeroIsBlackWhateverTheInkSays() {
    var rgb = JpegColorConverter.YcckOrCmykToRgb(
      [255], [255], [255], [0], 1, 1, JpegColorConverter.AdobeTransformNone);

    Assert.That(rgb[0], Is.EqualTo(0));
    Assert.That(rgb[1], Is.EqualTo(0));
    Assert.That(rgb[2], Is.EqualTo(0));
  }
}
