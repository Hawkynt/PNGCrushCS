using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The JCT-VC screen content coding conformance suite, decoded stream by stream.
/// </summary>
/// <remarks>
/// The suite is the bitstreams published at
/// <c>https://www.itu.int/wftp3/av-arch/jctvc-site/bitstream_exchange/draft_conformance/SCC/</c>,
/// fifteen archives of about two megabytes, most of them too large for the repository at full
/// length. Unpack them anywhere — one directory per archive, as they unpack — and point
/// <c>HEVC_SCC_CONFORMANCE</c> at the parent.
/// <para/>
/// Each stream is held to the checksum of the decoded output published beside it, which every
/// archive of this suite states for the planar file the HM decoder writes — in the coded plane
/// order, or for one RGB stream in R, G, B order. A stream that also carries decoded picture hash
/// SEI messages has to reproduce every one of them as well.
/// </remarks>
[TestFixture]
[Explicit("Needs the JCT-VC SCC conformance bitstreams; set HEVC_SCC_CONFORMANCE to the folder holding them.")]
public sealed class H265ScreenContentConformanceTests {

  private const string _VARIABLE = "HEVC_SCC_CONFORMANCE";

  private static IEnumerable<TestCaseData> _Streams() {
    var root = Environment.GetEnvironmentVariable(_VARIABLE);
    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
      yield return new TestCaseData("").SetName("SCC conformance (corpus not configured)");
      yield break;
    }

    foreach (var directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
      if (_FindBitstream(directory) != null)
        yield return new TestCaseData(directory).SetName("SCC " + Path.GetFileName(directory));
  }

  [TestCaseSource(nameof(_Streams))]
  public void DecodesAsTheEncoderReconstructedIt(string directory) {
    if (directory.Length == 0)
      Assert.Ignore($"Set {_VARIABLE} to a folder of unpacked JCT-VC SCC conformance archives.");

    var bitstream = _FindBitstream(directory)!;
    var expected = _ExpectedOutputChecksums(directory);

    var result = H265ConformanceDecoder.Decode(File.ReadAllBytes(bitstream));
    var sps = result.Sequence;
    var outputMatches = expected.Contains(result.OutputMd5)
                        || (sps?.ChromaArrayType == 3 && expected.Contains(result.OutputRgbMd5));

    TestContext.Out.WriteLine(
      $"{Path.GetFileName(bitstream)}: {sps?.Width}x{sps?.Height} chroma {sps?.ChromaArrayType} "
      + $"depth {sps?.BitDepthLuma}/{sps?.BitDepthChroma}, {result.OutputPictures} pictures out, "
      + $"{result.MatchingPictures}/{result.HashedPictures} picture hashes match, output MD5 {result.OutputMd5} "
      + (outputMatches ? "matches" : $"does not match {string.Join(", ", expected)}")
      + $"; tools: {string.Join(" ", result.Tools)}");

    Assert.Multiple(() => {
      Assert.That(result.FirstMismatch, Is.Null);
      Assert.That(result.MatchingPictures, Is.EqualTo(result.HashedPictures));
      Assert.That(outputMatches, Is.True, "The output matches no checksum beside the stream.");
    });
  }

  private static string? _FindBitstream(string directory)
    => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
      .Where(f => f.EndsWith(".bit", StringComparison.OrdinalIgnoreCase)
                  || f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
      .OrderBy(f => f, StringComparer.Ordinal)
      .FirstOrDefault();

  /// <summary>The MD5s the archive states for decoded output, leaving out the ones of the bitstream itself.</summary>
  private static HashSet<string> _ExpectedOutputChecksums(string directory) {
    var result = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) {
      if (!Path.GetFileName(file).Contains("md5", StringComparison.OrdinalIgnoreCase))
        continue;

      foreach (var line in File.ReadLines(file)) {
        var match = Regex.Match(line, @"^\s*([0-9a-fA-F]{32})\s*\*?(\S*)");
        if (!match.Success)
          continue;

        var name = match.Groups[2].Value;
        if (name.EndsWith(".bit", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
          continue;

        result.Add(match.Groups[1].Value.ToLowerInvariant());
      }
    }

    return result;
  }
}
