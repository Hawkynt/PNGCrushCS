using System;
using FileFormat.Core;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class MetadataTests {

  private static RawImage _Gray(int width, int height, ImageMetadata? metadata = null) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Gray8,
    PixelData = new byte[width * height],
    Metadata = metadata,
  };

  [Test]
  [Category("Integration")]
  public void RoundTrip_Description_SurvivesAsText() {
    var source = _Gray(4, 4, new() { TextEntries = [new("Description", "T1-weighted, 3T")] });
    var file = NiftiReader.FromBytes(NiftiWriter.ToBytes(NiftiFile.FromRawImage(source)));
    var decoded = NiftiFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Description.TrimEnd('\0'), Is.EqualTo("T1-weighted, 3T"));
      Assert.That(decoded.Metadata, Is.Not.Null);
      Assert.That(decoded.Metadata!.TextEntries[0].Text, Is.EqualTo("T1-weighted, 3T"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TrimsDescriptionToTheEightyCharactersTheHeaderHolds() {
    var long81 = new string('x', 81);
    var file = NiftiFile.FromRawImage(_Gray(2, 2, new() { TextEntries = [new("Description", long81)] }));

    Assert.That(file.Description, Has.Length.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AttachesNoMetadataWhenTheFieldIsEmpty() {
    var image = NiftiFile.ToRawImage(NiftiFile.FromRawImage(_Gray(2, 2)));

    Assert.That(image.Metadata, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TakesABareAnnotationWhenThatIsAllThereIs() {
    // A JPEG comment carries no keyword, and there is nothing else it could be describing.
    var file = NiftiFile.FromRawImage(_Gray(2, 2, new() { TextEntries = [new("", "shot on a Tuesday")] }));

    Assert.That(file.Description, Is.EqualTo("shot on a Tuesday"));
  }
}
