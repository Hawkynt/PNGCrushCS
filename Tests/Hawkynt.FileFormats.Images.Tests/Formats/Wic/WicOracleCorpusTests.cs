using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileFormat.Wic.Tests;

/// <summary>
/// Holds the reader and the writer to a local corpus decoded and encoded by the JWIC oracles.
/// </summary>
/// <remarks>
/// <para>
/// <c>WIC_CORPUS</c> names a directory of <c>.wic</c> files, each with the oracle's decode beside it
/// as <c>.pgm</c> or <c>.ppm</c> of the same name. The oracle is the portable core of the JWIC
/// translator sources (coder, zerotree, 2D heap, transform, conversions) built natively and driven
/// by a small decode/encode front end; on the three real samples its output is byte-identical to the
/// Java applet's. Every file must decode to exactly the oracle's pixels.
/// </para>
/// <para>
/// Files named <c>&lt;source&gt;_t&lt;T&gt;_g&lt;100|101&gt;_q&lt;Q&gt;.wic</c> were produced by the oracle's encoder from
/// <c>&lt;source&gt;.pgm/.ppm</c> in the sibling directory <c>wic-src</c> with transform T, header generation
/// and quality slider Q (budget 1.5·Q bits per pixel). Each of those must be reproduced byte for byte
/// by this package's writer.
/// </para>
/// </remarks>
[TestFixture]
[Explicit("Needs WIC_CORPUS pointing at a locally built oracle corpus.")]
[Category("Conformance")]
public sealed class WicOracleCorpusTests {

  private static readonly Regex _Encoded = new(@"^(?<source>.+)_t(?<t>\d)_g(?<g>100|101)_q(?<q>[0-9.]+)$", RegexOptions.CultureInvariant);

  private static string _Corpus() {
    var corpus = Environment.GetEnvironmentVariable("WIC_CORPUS");
    if (string.IsNullOrWhiteSpace(corpus) || !Directory.Exists(corpus))
      Assert.Ignore("Set WIC_CORPUS to a directory of .wic files with the oracle's .pgm/.ppm decodes beside them.");
    return corpus!;
  }

  [Test]
  public void EveryCorpusFile_DecodesToTheOraclesPixels() {
    var corpus = _Corpus();
    var failures = new List<string>();
    var checkedFiles = 0;
    foreach (var path in Directory.EnumerateFiles(corpus, "*.wic").OrderBy(static p => p, StringComparer.Ordinal)) {
      var expectedPath = File.Exists(Path.ChangeExtension(path, ".ppm")) ? Path.ChangeExtension(path, ".ppm") : Path.ChangeExtension(path, ".pgm");
      if (!File.Exists(expectedPath))
        continue;

      ++checkedFiles;
      var expected = WicTestSupport.ReadPnm(File.ReadAllBytes(expectedPath));
      try {
        var file = WicReader.FromSpan(File.ReadAllBytes(path));
        if (file.Width != expected.Width || file.Height != expected.Height || file.IsGrayscale == expected.Colour)
          failures.Add($"{Path.GetFileName(path)}: {file.Width}x{file.Height} grey={file.IsGrayscale}, oracle {expected.Width}x{expected.Height} colour={expected.Colour}");
        else if (!file.PixelData.AsSpan().SequenceEqual(expected.Pixels))
          failures.Add($"{Path.GetFileName(path)}: pixels differ, max {WicTestSupport.MaxDifference(file.PixelData, expected.Pixels)}");
      } catch (Exception exception) {
        failures.Add($"{Path.GetFileName(path)}: {exception.GetType().Name}: {exception.Message}");
      }
    }

    TestContext.Out.WriteLine($"{checkedFiles} files compared, {failures.Count} differ");
    Assert.That(checkedFiles, Is.GreaterThan(0));
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }

  [Test]
  public void EveryOracleEncodedFile_IsReproducedByteForByteByTheWriter() {
    var corpus = _Corpus();
    var sources = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(corpus).TrimEnd(Path.DirectorySeparatorChar))!, "wic-src");
    var failures = new List<string>();
    var checkedFiles = 0;
    foreach (var path in Directory.EnumerateFiles(corpus, "*.wic").OrderBy(static p => p, StringComparer.Ordinal)) {
      var match = _Encoded.Match(Path.GetFileNameWithoutExtension(path));
      if (!match.Success)
        continue;

      var source = Path.Combine(sources, match.Groups["source"].Value + ".ppm");
      if (!File.Exists(source))
        source = Path.ChangeExtension(source, ".pgm");
      if (!File.Exists(source))
        continue;

      ++checkedFiles;
      var picture = WicTestSupport.ReadPnm(File.ReadAllBytes(source));
      var quality = float.Parse(match.Groups["q"].Value, CultureInfo.InvariantCulture);
      var file = new WicFile {
        Width = picture.Width,
        Height = picture.Height,
        IsGrayscale = !picture.Colour,
        Generation = match.Groups["g"].Value == "100" ? WicGeneration.Version100 : WicGeneration.Version101,
        Transform = (WicTransform)int.Parse(match.Groups["t"].Value, CultureInfo.InvariantCulture),
        BitsPerPixelBudget = 1.5f * quality,
        PixelData = picture.Pixels,
      };

      var written = WicWriter.ToBytes(file);
      var oracle = File.ReadAllBytes(path);
      if (!written.AsSpan().SequenceEqual(oracle)) {
        var first = 0;
        while (first < Math.Min(written.Length, oracle.Length) && written[first] == oracle[first])
          ++first;
        failures.Add($"{Path.GetFileName(path)}: {written.Length} bytes vs oracle {oracle.Length}, first difference at {first}");
      }
    }

    TestContext.Out.WriteLine($"{checkedFiles} files re-encoded, {failures.Count} differ");
    Assert.That(checkedFiles, Is.GreaterThan(0));
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }
}
