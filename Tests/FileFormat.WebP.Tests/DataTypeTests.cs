using System.Collections.Generic;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void WebPFeatures_RecordStruct_StoresFields() {
    var features = new WebPFeatures(320, 240, true, true, false);

    Assert.That(features.Width, Is.EqualTo(320));
    Assert.That(features.Height, Is.EqualTo(240));
    Assert.That(features.HasAlpha, Is.True);
    Assert.That(features.IsLossless, Is.True);
    Assert.That(features.IsAnimated, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void WebPFile_DefaultValues() {
    var file = new WebPFile {
      Features = new WebPFeatures(1, 1, false, false, false)
    };

    Assert.That(file.ImageData, Is.Empty);
    Assert.That(file.IsLossless, Is.False);
    Assert.That(file.MetadataChunks, Is.Empty);
  }
}
