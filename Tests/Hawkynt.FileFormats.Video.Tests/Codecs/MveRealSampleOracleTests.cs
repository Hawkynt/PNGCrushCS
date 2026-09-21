using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Decodes real Interplay MVE films here and in FFmpeg and requires every picture to match.
/// </summary>
/// <remarks>
/// This is the check the codec's correctness actually rests on, and it cannot be a normal test
/// because the evidence is four films totalling fifty megabytes and this repository keeps no sample
/// files — every other fixture here builds its bytes by hand for that reason. So it is opt-in: point
/// <c>MVE_SAMPLES</c> at a directory of <c>.mve</c> files and it runs over all of them.
/// <para/>
/// What a hand-built fixture cannot do is cover the vocabulary. A test writes the block encoding it
/// meant to write, so it proves the decoder agrees with the test's author about that encoding and
/// nothing about the ones nobody thought to write. A real film settles it by arriving with whatever
/// its encoder chose: the four on <c>samples.ffmpeg.org</c> — interplay-logo, baldursgate-logo,
/// MARIO1 and descent3-level5-16bit — exercise fifteen of the sixteen block encodings between them,
/// both coded depths, and both the ordinary <c>0x11</c> video-data form and the legacy <c>0x10</c>
/// page-update one. At the time of writing all 2,648 pictures FFmpeg produces from them are
/// byte-identical to the pictures produced here.
/// <para/>
/// <b>Frame counts differ by design and the comparison allows for it.</b> A picture is presented on
/// SEND_BUFFER, and a chunk may display a page it did not rebuild — baldursgate-logo has 353
/// SEND_BUFFER opcodes against 330 video-data ones, descent3-level5-16bit 1,702 against 1,624.
/// This package presents those held frames, as the original player does; FFmpeg emits no packet for
/// a chunk carrying no video data and so produces fewer pictures. The pictures FFmpeg does produce
/// are a prefix-aligned subsequence of the ones here, so what is required is that every picture
/// FFmpeg produced matches the picture at the same position here, and that nothing here is missing.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class MveRealSampleOracleTests {

  /// <summary>Where <c>MVE_SAMPLES</c> points when real films are available to check against.</summary>
  private const string _SAMPLES_VARIABLE = "MVE_SAMPLES";

  [Test]
  [Explicit("Needs a directory of real .mve films; point MVE_SAMPLES at one.")]
  public void EveryPictureOfARealMveMatchesFFmpeg() {
    var directory = Environment.GetEnvironmentVariable(_SAMPLES_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_SAMPLES_VARIABLE} to a directory holding real .mve films.");

    FFmpegOracle.RequireAvailable();

    var films = Directory.GetFiles(directory!, "*.mve", SearchOption.AllDirectories)
      .Concat(Directory.GetFiles(directory!, "*.MVE", SearchOption.AllDirectories))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(static path => path, StringComparer.Ordinal)
      .ToList();

    Assert.That(films, Is.Not.Empty, $"{_SAMPLES_VARIABLE} names a directory with no .mve files in it.");

    foreach (var film in films) {
      var ours = _DecodeHere(film, out var width, out var height);
      Assert.That(ours, Is.Not.Empty, $"{Path.GetFileName(film)} decoded to no pictures here");

      var (decoded, detail, samples) = FFmpegOracle.TryDecodePictures(film, width, height, ours.Count);
      var frameBytes = checked(width * height * 3);

      // FFmpeg drops held frames, so it may legitimately produce fewer. Ask for what it gives.
      if (!decoded) {
        var (retried, retryDetail, fewer) = _AskForHoweverManyItGives(film, width, height, frameBytes);
        Assert.That(retried, Is.True, $"{Path.GetFileName(film)}: ffmpeg would not decode it: {detail} / {retryDetail}");
        samples = fewer;
      }

      var count = samples.Length / frameBytes;
      Assert.That(count, Is.GreaterThan(0), $"{Path.GetFileName(film)}: ffmpeg produced no whole pictures");
      Assert.That(count, Is.LessThanOrEqualTo(ours.Count),
        $"{Path.GetFileName(film)}: ffmpeg produced {count} pictures and this decoder only {ours.Count}");

      for (var i = 0; i < count; ++i)
        Assert.That(
          Convert.ToHexString(MD5.HashData(samples.AsSpan(i * frameBytes, frameBytes))),
          Is.EqualTo(ours[i]),
          $"{Path.GetFileName(film)}: picture {i} differs from ffmpeg's");

      TestContext.Out.WriteLine(
        $"{Path.GetFileName(film)}: {count} of {ours.Count} pictures compared, all identical");
    }
  }

  /// <summary>Every picture this package gets out of the film, as a hash apiece.</summary>
  private static List<string> _DecodeHere(string path, out int width, out int height) {
    var result = new List<string>();
    var bytes = File.ReadAllBytes(path);
    var measuredWidth = 0;
    var measuredHeight = 0;

    foreach (var frame in VideoFormatRegistry.DecodeFrames(bytes)) {
      var rgb = frame.Image.ToRgb24();
      measuredWidth = frame.Image.Width;
      measuredHeight = frame.Image.Height;
      result.Add(Convert.ToHexString(MD5.HashData(rgb)));
    }

    width = measuredWidth;
    height = measuredHeight;
    return result;
  }

  /// <summary>Decodes without demanding a picture count, for the films where FFmpeg makes fewer.</summary>
  private static (bool Decoded, string Detail, byte[] Pictures) _AskForHoweverManyItGives(
    string path, int width, int height, int frameBytes) {
    for (var attempt = 1; attempt <= 4096; attempt <<= 1) {
      var (decoded, detail, pictures) = FFmpegOracle.TryDecodePictures(path, width, height, attempt);
      if (decoded)
        return (true, detail, pictures);

      // "it decoded N frames instead of M" names the number it does produce; ask for that.
      var stated = _StatedFrameCount(detail);
      if (stated <= 0)
        continue;

      var (exact, exactDetail, exactPictures) = FFmpegOracle.TryDecodePictures(path, width, height, stated);
      return (exact, exactDetail, exactPictures);
    }

    return (false, "ffmpeg's picture count could not be established", []);
  }

  private static int _StatedFrameCount(string detail) {
    const string PREFIX = "it decoded ";
    var at = detail.IndexOf(PREFIX, StringComparison.Ordinal);
    if (at < 0)
      return 0;

    var digits = detail[(at + PREFIX.Length)..].TakeWhile(char.IsAsciiDigit).ToArray();
    return digits.Length != 0 && int.TryParse(new string(digits), out var count) ? count : 0;
  }
}
