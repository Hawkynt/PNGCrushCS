using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Pwc;

namespace FileFormat.Pwc.Tests;

/// <summary>
/// The checks that need the author's own programs or his sample pictures, neither of which this repository
/// may carry: <c>pwcdecode.exe</c> reading what this writer produced, and this reader decoding the fifteen
/// pictures of the PWC distribution exactly as <c>pwcdecode.exe</c> does.
/// </summary>
/// <remarks>
/// Point <c>PWC_DECODER</c> at <c>pwcdecode.exe</c> (run through Wine everywhere but Windows), and
/// <c>PWC_SAMPLES</c> at a directory of <c>.pwc</c> files, each with pwcdecode's <c>.bmp</c> decode beside
/// it (<c>pwcdecode *.pwc -batch</c> writes exactly that). At the time of writing all fifteen samples of
/// <c>pwc.zip</c> — benjerry, books, ccitt01, cmpndd, cmpndn, flax, gate, music, netscape, pattern,
/// sea_dusk, stone, sunset, winaw and yahoo — decode identically, and re-encoding each reproduces its file
/// byte for byte.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class PwcReferenceProgramTests {

  private const string _DECODER_VARIABLE = "PWC_DECODER";
  private const string _SAMPLES_VARIABLE = "PWC_SAMPLES";

  [Test]
  [Explicit("Needs the PWC distribution's samples with pwcdecode's decodes; point PWC_SAMPLES at them.")]
  public void EverySample_DecodesLikePwcdecodeAndReencodesToTheSameBytes() {
    var directory = Environment.GetEnvironmentVariable(_SAMPLES_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_SAMPLES_VARIABLE} to a directory of .pwc files with pwcdecode's .bmp beside each.");

    var samples = Directory.GetFiles(directory!, "*.pwc").OrderBy(static path => path, StringComparer.Ordinal).ToList();
    Assert.That(samples, Is.Not.Empty, $"{_SAMPLES_VARIABLE} names a directory with no .pwc files in it.");

    foreach (var sample in samples) {
      var bytes = File.ReadAllBytes(sample);
      var file = PwcReader.FromBytes(bytes);
      var reference = PwcFixtures.ReadBmp(File.ReadAllBytes(Path.ChangeExtension(sample, ".bmp")));
      Assert.That(file.PixelData, Is.EqualTo(reference.Indices), $"{Path.GetFileName(sample)} decodes differently");
      Assert.That(PwcWriter.ToBytes(file), Is.EqualTo(bytes), $"{Path.GetFileName(sample)} re-encodes differently");
    }
  }

  [TestCase(77, 41, 2)]
  [TestCase(45, 31, 16)]
  [TestCase(53, 29, 40)]
  [TestCase(64, 48, 256)]
  [Explicit("Needs pwcdecode.exe; point PWC_DECODER at it.")]
  public void Pwcdecode_ReadsWhatThisWriterWrote(int width, int height, int colors) {
    var decoder = Environment.GetEnvironmentVariable(_DECODER_VARIABLE);
    if (string.IsNullOrWhiteSpace(decoder) || !File.Exists(decoder))
      Assert.Inconclusive($"Set {_DECODER_VARIABLE} to pwcdecode.exe.");

    var raw = PwcWriterTests._Indexed(width, height, colors, width);
    var file = PwcFile.FromRawImage(raw);
    var directory = Directory.CreateTempSubdirectory("pwcoracle");
    try {
      var input = Path.Combine(directory.FullName, "ours.pwc");
      var output = Path.Combine(directory.FullName, "ours.bmp");
      File.WriteAllBytes(input, PwcWriter.ToBytes(file));
      _Run(decoder!, input, output);

      Assert.That(File.Exists(output), Is.True, "pwcdecode wrote no picture");
      var decoded = PwcFixtures.ReadBmp(File.ReadAllBytes(output));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Indices, Is.EqualTo(raw.PixelData), "pwcdecode rebuilt a different picture");
    } finally {
      directory.Delete(true);
    }
  }

  private static void _Run(string decoder, string input, string output) {
    var windows = OperatingSystem.IsWindows();
    var start = new ProcessStartInfo(windows ? decoder : "wine") { RedirectStandardOutput = true, RedirectStandardError = true };
    if (!windows)
      start.ArgumentList.Add(decoder);

    start.ArgumentList.Add(windows ? input : "Z:" + input.Replace('/', '\\'));
    start.ArgumentList.Add(windows ? output : "Z:" + output.Replace('/', '\\'));
    start.ArgumentList.Add("-nolicense");
    start.Environment["WINEDEBUG"] = "-all";

    using var process = Process.Start(start)!;
    process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    if (!process.WaitForExit(60_000))
      process.Kill(true);
  }
}
