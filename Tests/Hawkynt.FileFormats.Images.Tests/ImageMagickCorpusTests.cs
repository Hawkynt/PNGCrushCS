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

  /// <summary>
  /// Extensions where the reference tool writes something other than what we read, or a format we
  /// knowingly do not implement. Each is a decision rather than an oversight, so each carries why.
  /// </summary>
  /// <remarks>
  /// Sharing an extension does not make two things the same format. The reference tool writes its
  /// own textual report to <c>.info</c> and a directory listing to <c>.vid</c>; ours are the Amiga
  /// icon and the video-frame formats of the same names. Counting those as failures would measure
  /// the coincidence, not the reader.
  /// </remarks>
  private static readonly Dictionary<string, string> _NotOurs = new(StringComparer.OrdinalIgnoreCase) {
    [".info"] = "the reference tool writes its identify report here, not a picture",
    [".vid"] = "the reference tool writes a directory of thumbnails, not a picture",
    [".map"] = "a bare palette with no picture and no header",
    [".rgb"] = "headerless samples: nothing in the file gives its size",
    [".rgba"] = "headerless samples: nothing in the file gives its size",
    [".yuv"] = "headerless samples: nothing in the file gives its size",
    [".eps"] = "PostScript, which needs an interpreter rather than a decoder",
    [".epi"] = "PostScript, which needs an interpreter rather than a decoder",
    [".epsf"] = "PostScript, which needs an interpreter rather than a decoder",
    [".epsi"] = "PostScript, which needs an interpreter rather than a decoder",
    [".ept"] = "PostScript with a preview, which needs an interpreter for the picture itself",
  };

  [TestCaseSource(nameof(Samples))]
  public void SampleWrittenByAnotherTool_Decodes(string? path) {
    if (path == null)
      Assert.Ignore("Set IMAGEMAGICK_CORPUS to a directory of reference samples.");

    if (_NotOurs.TryGetValue(Path.GetExtension(path!), out var why))
      Assert.Ignore(why);

    var extension = Path.GetExtension(path!);
    if (FormatRegistry.DetectCandidatesFromExtension(extension).Count == 0)
      Assert.Ignore($"{extension} is not registered.");

    // Read the way a caller would, which tries magic bytes and then every format the extension
    // names — an extension shared by several formats resolves only by trying them.
    var image = FormatRegistry.Read(new FileInfo(path!));

    Assert.That(image, Is.Not.Null, $"{extension}: reader returned nothing");
    Assert.Multiple(() => {
      Assert.That(image!.Width, Is.GreaterThan(0), $"{extension}: no width");
      Assert.That(image.Height, Is.GreaterThan(0), $"{extension}: no height");
      Assert.That(image.PixelData, Is.Not.Null.And.Not.Empty, $"{extension}: no pixels");
    });
  }
}
