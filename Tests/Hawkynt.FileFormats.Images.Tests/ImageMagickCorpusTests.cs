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
    [".g3"] = "a bare fax stream states no page size; we take the fax scan line and the reference tool an A4 page",
    [".g4"] = "a bare fax stream states no page size; we take the fax scan line and the reference tool an A4 page",
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
    var file = new FileInfo(path!);
    var image = FormatRegistry.Read(file);

    // On failure, ask each candidate again without the null-on-error wrapper, so the report says
    // what went wrong rather than only that something did.
    if (image == null) {
      var reasons = new List<string>();
      foreach (var candidate in FormatRegistry.DetectCandidatesFromExtension(extension)) {
        var reader = FormatRegistry.GetEntry(candidate)?.LoadRawImageOrThrow;
        if (reader == null)
          continue;

        try {
          reader(file);
        } catch (Exception failure) {
          reasons.Add($"{candidate}: {failure.GetType().Name}: {failure.Message}");
        }
      }

      Assert.Fail($"{extension}: {(reasons.Count > 0 ? string.Join(" | ", reasons) : "no reader accepted it")}");
    }

    Assert.That(image, Is.Not.Null, $"{extension}: reader returned nothing");
    Assert.Multiple(() => {
      Assert.That(image!.Width, Is.GreaterThan(0), $"{extension}: no width");
      Assert.That(image.Height, Is.GreaterThan(0), $"{extension}: no height");
      Assert.That(image.PixelData, Is.Not.Null.And.Not.Empty, $"{extension}: no pixels");
    });

    _CompareWithReference(path!, extension, image!);
  }

  /// <summary>Checks our pixels against the ones the reference tool got from the same file.</summary>
  /// <remarks>
  /// Opening a file is the weaker half of reading it. A palette read backwards, a channel widened by
  /// division rather than bit repetition, rows off by one — none of those throw, and every one of
  /// them has shipped here before. The corpus therefore carries the reference tool's own decode of
  /// each sample as a PNG beside it, and the two are compared pixel for pixel.
  /// </remarks>
  private static void _CompareWithReference(string path, string extension, RawImage ours) {
    var reference = new FileInfo(path + ".ref.png");
    if (!reference.Exists)
      return;

    var theirs = FormatRegistry.Read(reference);
    if (theirs == null) {
      Assert.Warn($"{extension}: the reference decode beside it could not be read");
      return;
    }

    Assert.That(
      (ours.Width, ours.Height), Is.EqualTo((theirs.Width, theirs.Height)),
      $"{extension}: we read {ours.Width}x{ours.Height} where the reference tool read {theirs.Width}x{theirs.Height}");

    var a = PixelConverter.Convert(ours, PixelFormat.Bgra32).PixelData;
    var b = PixelConverter.Convert(theirs, PixelFormat.Bgra32).PixelData;

    int worst = 0, differing = 0;
    var at = -1;
    for (var i = 0; i < a.Length && i < b.Length; ++i) {
      // Alpha is skipped: the reference decodes are written without it, so a format carrying one
      // would be compared against a channel the reference simply does not have.
      if ((i & 3) == 3)
        continue;

      var delta = Math.Abs(a[i] - b[i]);
      if (delta <= _ChannelTolerance)
        continue;

      ++differing;
      if (delta <= worst)
        continue;

      worst = delta;
      at = i >> 2;
    }

    if (differing == 0)
      return;

    var pixels = Math.Min(a.Length, b.Length) / 4;
    Assert.Fail(
      $"{extension}: {differing} of {pixels * 3} channel samples differ from the reference decode, "
      + $"worst by {worst} at pixel {at % ours.Width},{at / ours.Width} "
      + $"(ours {a[at * 4 + 2]},{a[at * 4 + 1]},{a[at * 4]} vs {b[at * 4 + 2]},{b[at * 4 + 1]},{b[at * 4]})");
  }

  /// <summary>
  /// How far a channel may sit from the reference before it counts as a disagreement.
  /// </summary>
  /// <remarks>
  /// Not zero, because the two sides round differently where a format stores fewer than eight bits a
  /// channel, and because a lossy codec decoded by two implementations is allowed to differ in the
  /// last step. It is small enough that a palette in the wrong order, an inverted ramp or a shifted
  /// row cannot hide under it.
  /// </remarks>
  private const int _ChannelTolerance = 4;
}
