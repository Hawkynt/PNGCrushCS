using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.JpegXr.JxrLib;

namespace FileFormat.JpegXr.Tests;

/// <summary>
/// Opt-in conformance run against a directory of <c>X.jxr</c> files paired with <c>X.raw</c>, the native-format
/// decode produced by Microsoft's jxrlib (rows packed, stride = ceil(width * bpp / 8)). Point the environment
/// variable <c>JXR_CORPUS</c> at the directory.
/// </summary>
[TestFixture]
public sealed class JxrLibConformanceTests {

  private static string? _Corpus => Environment.GetEnvironmentVariable("JXR_CORPUS");

  [Test]
  [Explicit("Needs a jxrlib reference corpus in JXR_CORPUS")]
  [Category("Regression")]
  public void Corpus_DecodesBitExactWithJxrLib() {
    var dir = _Corpus;
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
      Assert.Ignore("JXR_CORPUS not set");

    var files = Directory.GetFiles(dir!, "*.jxr").OrderBy(f => f, StringComparer.Ordinal).ToArray();
    Assert.That(files, Is.Not.Empty);

    var failures = new List<string>();
    var passed = 0;
    foreach (var jxr in files) {
      var name = Path.GetFileNameWithoutExtension(jxr);
      var raw = Path.ChangeExtension(jxr, ".raw");
      if (!File.Exists(raw))
        continue;

      var expected = File.ReadAllBytes(raw);
      try {
        var image = JxrLibDecoder.Decode(File.ReadAllBytes(jxr));
        var actual = image.Pixels;
        var length = Math.Min(actual.Length, expected.Length);
        var first = -1;
        for (var i = 0; i < length; ++i)
          if (actual[i] != expected[i]) {
            first = i;
            break;
          }

        if (first < 0 && actual.Length == expected.Length) {
          ++passed;
          continue;
        }

        if (first < 0)
          failures.Add($"{name}: length {actual.Length} != {expected.Length}");
        else {
          var px = first / Math.Max(1, image.BitsPerPixel / 8);
          failures.Add($"{name} ({image.PixelFormatName} {image.Width}x{image.Height}): byte {first} (row {first / image.Stride}, col {(first % image.Stride) / Math.Max(1, image.BitsPerPixel / 8)}, px {px}) got {actual[first]:x2} want {expected[first]:x2}");
        }
      } catch (Exception e) {
        failures.Add($"{name}: {e.GetType().Name}: {e.Message}");
      }
    }

    TestContext.Out.WriteLine($"passed {passed}/{passed + failures.Count}");
    foreach (var f in failures)
      TestContext.Out.WriteLine(f);
    Assert.That(failures, Is.Empty, $"{failures.Count} of {passed + failures.Count} files differ");
  }

  [Test]
  [Explicit("Needs a jxrlib reference corpus in JXR_CORPUS")]
  [Category("Regression")]
  public void Corpus_CorruptedFiles_FailCleanly() {
    var dir = _Corpus;
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
      Assert.Ignore("JXR_CORPUS not set");

    var rng = new Random(20261127);
    var unexpected = new List<string>();
    foreach (var jxr in Directory.GetFiles(dir!, "*.jxr").OrderBy(f => f, StringComparer.Ordinal)) {
      var original = File.ReadAllBytes(jxr);
      for (var round = 0; round < 8; ++round) {
        var data = (byte[])original.Clone();
        if (round == 0)
          Array.Resize(ref data, data.Length / 2);
        else
          for (var k = 0; k < 1 + round; ++k)
            data[rng.Next(data.Length)] ^= (byte)(1 << rng.Next(8));

        try {
          JxrLibDecoder.Decode(data);
        } catch (InvalidDataException) {
        } catch (NotSupportedException) {
        } catch (Exception e) {
          unexpected.Add($"{Path.GetFileName(jxr)} round {round}: {e.GetType().Name}: {e.Message}");
        }
      }
    }

    foreach (var u in unexpected)
      TestContext.Out.WriteLine(u);
    Assert.That(unexpected, Is.Empty);
  }
}
