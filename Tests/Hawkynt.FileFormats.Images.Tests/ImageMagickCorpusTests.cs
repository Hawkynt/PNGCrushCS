using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Reads a directory of files another tool wrote, one per format, and checks we can decode each.
/// </summary>
/// <remarks>
/// Claiming to read a format means listing its extension; proving it means opening a file somebody
/// else produced. Those are different facts, and only the second one is worth anything to a user —
/// a reader tested solely against our own writer agrees with itself however wrong it is, which is
/// the failure this project has hit repeatedly.
/// <para/>
/// The corpus is generated rather than committed: point <c>IMAGEMAGICK_CORPUS</c> at a directory of
/// <c>s_&lt;ext&gt;.&lt;ext&gt;</c> files, all showing the same picture, and every one is opened by
/// the format its extension names.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class ImageMagickCorpusTests {

  private static string? CorpusPath => Environment.GetEnvironmentVariable("IMAGEMAGICK_CORPUS");

  private static IEnumerable<TestCaseData> Samples() {
    var path = CorpusPath;
    if (path == null || !Directory.Exists(path)) {
      yield return new TestCaseData((string?)null).SetName("{m}(no corpus)");
      yield break;
    }

    foreach (var file in Directory.EnumerateFiles(path, "s_*.*").OrderBy(f => f))
      yield return new TestCaseData(file).SetName($"{{m}}({Path.GetExtension(file)})");
  }

  [TestCaseSource(nameof(Samples))]
  public void SampleWrittenByAnotherTool_Decodes(string? path) {
    if (path == null)
      Assert.Ignore("Set IMAGEMAGICK_CORPUS to a directory of reference samples.");

    var extension = Path.GetExtension(path!);
    var format = FormatRegistry.DetectFromExtension(extension);
    if (format == ImageFormat.Unknown)
      Assert.Ignore($"{extension} is not registered.");

    var entry = FormatRegistry.GetEntry(format);
    if (entry?.LoadRawImage == null)
      Assert.Ignore($"{format} has no reader.");

    RawImage? image;
    try {
      image = entry.LoadRawImage(new FileInfo(path!));
    } catch (Exception failure) {
      Assert.Fail($"{extension}: {failure.GetType().Name}: {failure.Message}");
      return;
    }

    Assert.That(image, Is.Not.Null, $"{extension}: reader returned nothing");
    Assert.Multiple(() => {
      Assert.That(image!.Width, Is.GreaterThan(0), $"{extension}: no width");
      Assert.That(image.Height, Is.GreaterThan(0), $"{extension}: no height");
      Assert.That(image.PixelData, Is.Not.Null.And.Not.Empty, $"{extension}: no pixels");
    });
  }
}
