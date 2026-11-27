using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The JCT-VC format range extensions conformance suite, decoded stream by stream.
/// </summary>
/// <remarks>
/// The suite is the bitstreams published at
/// <c>https://www.itu.int/wftp3/av-arch/jctvc-site/bitstream_exchange/draft_conformance/RExt/</c>,
/// about fifty archives of seventy-odd megabytes, which is why it is not in the repository. Unpack
/// them anywhere — one directory per archive, as they unpack — and point
/// <c>HEVC_REXT_CONFORMANCE</c> at the parent.
/// <para/>
/// Each stream is held to two oracles. The decoded picture hash SEI messages its encoder wrote are
/// the strict one: every picture that carries a hash has to reproduce it, plane by plane. The
/// checksum file beside the stream is the second, and is reported rather than required where the
/// stream carries hashes, because the suite's checksum files were written by several tools and some
/// describe a layout no decoder writes by default — interleaved RGB, chroma padded out to 4:4:4, or
/// a different stream altogether. A stream without hashes has only its checksum file, and is held
/// to that.
/// </remarks>
[TestFixture]
[Explicit("Needs the JCT-VC RExt conformance bitstreams; set HEVC_REXT_CONFORMANCE to the folder holding them.")]
public sealed class H265RangeExtensionConformanceTests {

  private const string _VARIABLE = "HEVC_REXT_CONFORMANCE";

  private static IEnumerable<TestCaseData> _Streams() {
    var root = Environment.GetEnvironmentVariable(_VARIABLE);
    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
      yield return new TestCaseData("").SetName("RExt conformance (corpus not configured)");
      yield break;
    }

    foreach (var directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
      if (_FindBitstream(directory) != null)
        yield return new TestCaseData(directory).SetName("RExt " + Path.GetFileName(directory));
  }

  [TestCaseSource(nameof(_Streams))]
  public void DecodesAsTheEncoderReconstructedIt(string directory) {
    if (directory.Length == 0)
      Assert.Ignore($"Set {_VARIABLE} to a folder of unpacked JCT-VC RExt conformance archives.");

    var bitstream = _FindBitstream(directory)!;
    var expected = _ExpectedOutputChecksums(directory);

    var result = H265ConformanceDecoder.Decode(File.ReadAllBytes(bitstream));
    var sps = result.Sequence;
    var outputMatches = expected.Contains(result.OutputMd5);

    TestContext.Out.WriteLine(
      $"{Path.GetFileName(bitstream)}: {sps?.Width}x{sps?.Height} chroma {sps?.ChromaArrayType} "
      + $"depth {sps?.BitDepthLuma}/{sps?.BitDepthChroma}, {result.OutputPictures} pictures out, "
      + $"{result.MatchingPictures}/{result.HashedPictures} picture hashes match, output MD5 {result.OutputMd5} "
      + (outputMatches ? "matches" : $"does not match {string.Join(", ", expected)}")
      + $"; tools: {string.Join(" ", result.Tools)}");

    if (result.HashedPictures == 0) {
      Assert.That(outputMatches, Is.True,
        "The stream carries no decoded picture hash, and the output matches no checksum beside it.");
      return;
    }

    Assert.That(result.FirstMismatch, Is.Null);
    Assert.That(result.MatchingPictures, Is.EqualTo(result.HashedPictures));
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
