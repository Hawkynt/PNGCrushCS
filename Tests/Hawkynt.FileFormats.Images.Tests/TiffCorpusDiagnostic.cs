using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Tiff;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Surfaces why a reference TIFF fails, which the registry's null-on-error hides.</summary>
[TestFixture]
[Category("Conformance")]
public sealed class TiffCorpusDiagnostic {

  [Test]
  public void ReferenceTiff_Decodes() {
    var corpus = Environment.GetEnvironmentVariable("IMAGEMAGICK_CORPUS");
    if (corpus == null || !Directory.Exists(corpus))
      Assert.Ignore("Set IMAGEMAGICK_CORPUS to a directory of reference samples.");

    var path = Path.Combine(corpus!, "s_tiff.tiff");
    if (!File.Exists(path))
      Assert.Ignore("No reference TIFF in the corpus.");

    var file = TiffReader.FromBytes(File.ReadAllBytes(path));
    var image = TiffFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(13));
      Assert.That(image.Height, Is.EqualTo(7));
    });
  }
}
