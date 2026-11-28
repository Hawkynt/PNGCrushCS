using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.Iwc.Tests;

/// <summary>
/// Holds the reader to WaveL's own files and the writer to WaveL's own decoder, on a local corpus.
/// </summary>
/// <remarks>
/// <c>IWC_CORPUS</c> names a directory of <c>.iwc</c> files, each with the applet's decode beside it as
/// a <c>.ppm</c> of the same name; every file must decode to exactly those pixels. <c>IWC_ORACLE</c>
/// names a command that runs the applet's classes on one file (see <see cref="IwcTestSupport.RunOracle"/>);
/// with it, every corpus picture is re-encoded by this writer and the applet has to rebuild exactly
/// what this reader rebuilds.
/// </remarks>
[TestFixture]
[Explicit("Needs IWC_CORPUS (and IWC_ORACLE) pointing at a local corpus and the WaveL applet.")]
[Category("Conformance")]
public sealed class IwcOracleCorpusTests {

  private static string _Corpus() {
    var corpus = Environment.GetEnvironmentVariable("IWC_CORPUS");
    if (string.IsNullOrWhiteSpace(corpus) || !Directory.Exists(corpus))
      Assert.Ignore("Set IWC_CORPUS to a directory of .iwc files with the applet's .ppm decodes beside them.");
    return corpus!;
  }

  [Test]
  public void EveryCorpusFile_DecodesToTheAppletsPixels() {
    var failures = new List<string>();
    var compared = 0;
    foreach (var path in Directory.EnumerateFiles(_Corpus(), "*.iwc").OrderBy(static p => p, StringComparer.Ordinal)) {
      var expectedPath = Path.ChangeExtension(path, ".ppm");
      if (!File.Exists(expectedPath))
        continue;

      ++compared;
      var expected = IwcTestSupport.ReadPpm(File.ReadAllBytes(expectedPath));
      try {
        var file = IwcReader.FromSpan(File.ReadAllBytes(path));
        if ((file.Width, file.Height) != (expected.Width, expected.Height))
          failures.Add($"{Path.GetFileName(path)}: {file.Width}x{file.Height}, applet {expected.Width}x{expected.Height}");
        else if (!file.PixelData.AsSpan().SequenceEqual(expected.Pixels))
          failures.Add($"{Path.GetFileName(path)}: pixels differ, max {IwcTestSupport.MaxDifference(file.PixelData, expected.Pixels)}");
      } catch (Exception exception) {
        failures.Add($"{Path.GetFileName(path)}: {exception.GetType().Name}: {exception.Message}");
      }
    }

    TestContext.Out.WriteLine($"{compared} files compared, {failures.Count} differ");
    Assert.That(compared, Is.GreaterThan(0));
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }

  [Test]
  public void EveryCorpusPicture_ReEncoded_IsRebuiltByTheAppletAsByThisReader() {
    var corpus = _Corpus();
    if (IwcTestSupport.RunOracle(Path.Combine(Path.GetTempPath(), "missing.iwc")) == null)
      Assert.Ignore("Set IWC_ORACLE to a command that decodes one .iwc into a .ppm with the WaveL applet.");

    var directory = Directory.CreateTempSubdirectory("iwcoracle");
    var failures = new List<string>();
    var compared = 0;
    try {
      foreach (var path in Directory.EnumerateFiles(corpus, "*.ppm").Where(static p => !p.EndsWith(".oracle.ppm", StringComparison.Ordinal))) {
        var picture = IwcTestSupport.ReadPpm(File.ReadAllBytes(path));
        var bytes = IwcWriter.ToBytes(new IwcFile { Width = picture.Width, Height = picture.Height, PixelData = picture.Pixels });
        var written = Path.Combine(directory.FullName, Path.GetFileNameWithoutExtension(path) + ".iwc");
        File.WriteAllBytes(written, bytes);

        ++compared;
        var ours = IwcReader.FromSpan(bytes);
        var oracle = IwcTestSupport.RunOracle(written)!;
        if (oracle.Length == 0) {
          failures.Add($"{Path.GetFileName(path)}: the applet produced nothing");
          continue;
        }

        var applet = IwcTestSupport.ReadPpm(oracle);
        if (!applet.Pixels.AsSpan().SequenceEqual(ours.PixelData))
          failures.Add($"{Path.GetFileName(path)}: applet and reader disagree, max {IwcTestSupport.MaxDifference(applet.Pixels, ours.PixelData)}");

        TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {bytes.Length} bytes, max error against the source {IwcTestSupport.MaxDifference(ours.PixelData, picture.Pixels)}");
      }
    } finally {
      directory.Delete(true);
    }

    Assert.That(compared, Is.GreaterThan(0));
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }
}
