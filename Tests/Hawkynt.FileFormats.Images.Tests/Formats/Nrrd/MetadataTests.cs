using System;
using FileFormat.Core;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class MetadataTests {

  private static RawImage _Gray(ImageMetadata? metadata = null) => new() {
    Width = 4,
    Height = 3,
    Format = PixelFormat.Gray8,
    PixelData = new byte[12],
    Metadata = metadata,
  };

  [Test]
  [Category("Integration")]
  public void RoundTrip_AxisLabels_SurviveAsText() {
    var source = _Gray(new() { TextEntries = [new("Label", "x"), new("Label", "y")] });
    var file = NrrdReader.FromBytes(NrrdWriter.ToBytes(NrrdFile.FromRawImage(source)));
    var decoded = NrrdFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Labels, Is.EqualTo(new[] { "x", "y" }));
      Assert.That(decoded.Metadata, Is.Not.Null);
      Assert.That(decoded.Metadata!.TextEntries.Count, Is.EqualTo(2));
      Assert.That(decoded.Metadata.TextEntries[1].Text, Is.EqualTo("y"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_DropsLabelsThatDoNotNameEveryAxis() {
    // A colour picture gains a channel axis, so two labels no longer name the three axes there are
    // — and a per-axis field that names the wrong axes is worse than none.
    var color = new RawImage {
      Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36],
      Metadata = new() { TextEntries = [new("Label", "x"), new("Label", "y")] },
    };

    Assert.That(NrrdFile.FromRawImage(color).Labels, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_DropsLabelsThatWouldBreakTheHeaderTheyGoIn() {
    var source = _Gray(new() { TextEntries = [new("Label", "x"), new("Label", "a \" quote")] });

    Assert.That(NrrdFile.FromRawImage(source).Labels, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AttachesNoMetadataWhenThereAreNoLabels() {
    Assert.That(NrrdFile.ToRawImage(NrrdFile.FromRawImage(_Gray())).Metadata, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SaysWhichAxisHoldsTheColour() {
    // Without the kinds field, a reader has no reason to think a 3 by w by h array is a picture
    // rather than three of them.
    var color = new RawImage {
      Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36]
    };

    var header = System.Text.Encoding.ASCII.GetString(NrrdWriter.ToBytes(NrrdFile.FromRawImage(color)), 0, 120);

    Assert.That(header, Does.Contain("kinds: RGB-color domain domain"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SaysNothingAboutKindsForAPlainGreyPicture() {
    // Two spatial axes is what a missing kinds field already means.
    var header = System.Text.Encoding.ASCII.GetString(NrrdWriter.ToBytes(NrrdFile.FromRawImage(_Gray())), 0, 60);

    Assert.That(header, Does.Not.Contain("kinds"));
  }
}
