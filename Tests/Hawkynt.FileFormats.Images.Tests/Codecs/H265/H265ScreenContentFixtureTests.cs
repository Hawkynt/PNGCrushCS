using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The screen content coding tools, each decoded from a stream that uses it and held to the HM-SCM
/// reference decoder.
/// </summary>
/// <remarks>
/// Eight streams of the JCT-VC SCC conformance suite and seven the HM-SCM encoder wrote for what
/// that suite leaves out — lossless palette escapes and colour transform, 4:2:2 and monochrome
/// palettes, weighted prediction beside intra block copy, luma and chroma of different depths, and
/// wavefronts with dependent slice segments. <c>Fixtures/README.md</c> says where each came from.
/// Each stream is held to the MD5 of the file the HM-SCM decoder writes for it, and to every decoded
/// picture hash its encoder wrote. The full suite is <see cref="H265ScreenContentConformanceTests"/>.
/// </remarks>
[TestFixture]
public sealed class H265ScreenContentFixtureTests {

  private static string _Directory => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "H265");

  private static IEnumerable<TestCaseData> _Fixtures() {
    var manifest = Path.Combine(AppContext.BaseDirectory, "Fixtures", "H265", "expected-scc.md5");
    foreach (var line in File.ReadLines(manifest)) {
      var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (fields.Length == 2)
        yield return new TestCaseData(fields[1], fields[0]).SetName($"SCC fixture {fields[1]}");
    }
  }

  [TestCaseSource(nameof(_Fixtures))]
  [Category("Unit")]
  public void DecodesAsTheReferenceDecoderDoes(string name, string expectedMd5) {
    var path = Path.Combine(_Directory, name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");

    var result = H265ConformanceDecoder.Decode(File.ReadAllBytes(path));

    Assert.Multiple(() => {
      Assert.That(result.FirstMismatch, Is.Null, "a decoded picture hash SEI message disagrees");
      Assert.That(result.MatchingPictures, Is.EqualTo(result.HashedPictures));
      Assert.That(result.OutputPictures, Is.GreaterThan(0));
      Assert.That(result.OutputMd5, Is.EqualTo(expectedMd5), "the output differs from the HM-SCM decoder's");
    });
  }
}
