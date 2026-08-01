using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Miff;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Surfaces why a reference MIFF fails, which the registry's null-on-error hides.</summary>
[TestFixture]
[Category("Conformance")]
public sealed class MiffCorpusDiagnostic {

  [Test]
  public void ReferenceMiff_Decodes() {
    var corpus = Environment.GetEnvironmentVariable("IMAGEMAGICK_CORPUS");
    if (corpus == null || !Directory.Exists(corpus))
      Assert.Ignore("Set IMAGEMAGICK_CORPUS to a directory of reference samples.");

    var path = Path.Combine(corpus!, "s_miff.miff");
    if (!File.Exists(path))
      Assert.Ignore("No reference MIFF in the corpus.");

    var image = MiffFile.ToRawImage(MiffReader.FromBytes(File.ReadAllBytes(path)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(13));
      Assert.That(image.Height, Is.EqualTo(7));
    });
  }
}
