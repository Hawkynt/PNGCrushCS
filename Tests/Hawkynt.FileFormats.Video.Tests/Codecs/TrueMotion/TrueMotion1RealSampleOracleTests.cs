using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Decodes real Duck TrueMotion 1 films here and in FFmpeg and requires every picture to match.
/// </summary>
/// <remarks>
/// Opt-in for the reason every real-sample check here is: the evidence is a directory of films and
/// this repository keeps none. Point <c>TRUEMOTION1_SAMPLES</c> at a copy of
/// <c>samples.ffmpeg.org/V-codecs/DUCK/</c> — eleven films in AVI and QuickTime, ten at sixteen bits
/// and one at twenty-four, 288x144 to 336x168.
/// <para/>
/// The comparison is on the coded samples, not on a conversion. FFmpeg is asked for its native layout,
/// <c>rgb555le</c> or <c>bgr0</c>, and its words are widened to RGB24 the way this decoder widens its
/// own, so a match is a match of every coded bit and a difference cannot hide in a scaler.
/// <para/>
/// FFmpeg's demuxers drop the zero-length chunks an AVI index can list, and its decoder therefore
/// produces one picture per non-empty packet; this decoder holds the previous picture for an empty
/// one, as a player does. Pictures are compared per non-empty packet.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class TrueMotion1RealSampleOracleTests {

  private const string _SAMPLES_VARIABLE = "TRUEMOTION1_SAMPLES";

  [Test]
  [Explicit("Needs a directory of real TrueMotion 1 films; point TRUEMOTION1_SAMPLES at one.")]
  public void EveryPictureOfARealTrueMotion1FilmMatchesFFmpeg() {
    var directory = Environment.GetEnvironmentVariable(_SAMPLES_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_SAMPLES_VARIABLE} to a directory holding real TrueMotion 1 films.");

    FFmpegOracle.RequireAvailable();

    var films = Directory.GetFiles(directory!).OrderBy(static path => path, StringComparer.Ordinal).ToList();
    Assert.That(films, Is.Not.Empty);

    var total = 0;
    foreach (var film in films) {
      var name = Path.GetFileName(film);
      var bytes = File.ReadAllBytes(film);
      var stream = VideoFormatRegistry.ReadStreams(bytes).First(static s => s.Kind == MediaStreamKind.Video);
      var decoder = (TrueMotion1VideoDecoder)VideoFormatRegistry.CreateDecoder(stream);

      var packets = VideoFormatRegistry.ReadPackets(bytes, stream.Index).Where(static p => p.Data.Length != 0).ToList();
      var ours = packets.Select(packet => {
        Assert.That(decoder.TryDecode(packet, out var picture), Is.True);
        return picture;
      }).ToList();

      Assert.That(ours, Is.Not.Empty, $"{name} decoded to no pictures here");
      var native = _IsTwentyFourBit(bytes, stream) ? ("bgr0", 4) : ("rgb555le", 2);

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, native.Item1);
      Assert.That(decoded, Is.True, $"{name}: ffmpeg would not decode it: {detail}");

      var theirSizes = _FFmpegsPictureSizes(packets);
      var expected = theirSizes.Sum(size => (long)size.Width * size.Height * native.Item2);
      Assert.That(samples.LongLength, Is.EqualTo(expected),
        $"{name}: ffmpeg's pictures do not add up to the {ours.Count} pictures it should have decoded");

      var at = 0;
      var skipped = 0;
      for (var i = 0; i < ours.Count; ++i) {
        var (theirWidth, theirHeight, comparable) = theirSizes[i];
        var frameBytes = theirWidth * theirHeight * native.Item2;
        var theirs = _ToRgb24(samples.AsSpan(at, frameBytes), native.Item2);
        at += frameBytes;

        if (!comparable) {
          // FFmpeg discarded its picture here (see _FFmpegsPictureSizes); what this decoder must do
          // instead is carry the picture on, which is checked against its own previous one.
          ++skipped;
          if (_IsEmptyFrame(packets[i]))
            Assert.That(ours[i].PixelData, Is.EqualTo(ours[i - 1].PixelData),
              $"{name}: picture {i} codes nothing and must repeat the picture before it");
          continue;
        }

        if (!theirs.AsSpan().SequenceEqual(ours[i].PixelData))
          Assert.Fail($"{name}: picture {i} of {ours.Count} ({ours[i].Width}x{ours[i].Height}) differs from ffmpeg's "
                      + $"(first difference at byte {_FirstDifference(theirs, ours[i].PixelData)})");
      }

      var sizes = string.Join(", ", ours.Select(static p => $"{p.Width}x{p.Height}").Distinct());
      TestContext.Out.WriteLine(
        $"{name}: {sizes}, {ours.Count - skipped} of {ours.Count} pictures identical to ffmpeg"
        + (skipped == 0 ? "" : $", {skipped} where ffmpeg discards its picture after an empty frame"));
      total += ours.Count;
    }

    TestContext.Out.WriteLine($"{films.Count} films, {total} pictures");
  }

  private static bool _IsEmptyFrame(CodedPacket packet)
    => TrueMotion1Format.CompressionTypes[TrueMotion1Format.ReadHeader(packet.Data.Span).Compression].Algorithm
       == TrueMotion1Format.Algorithm.None;

  /// <summary>
  /// The size of every picture FFmpeg emits, and whether it can be compared with this decoder's.
  /// </summary>
  /// <remarks>
  /// FFmpeg sizes a frame whose compression type codes nothing as a sixteen-bit picture at the
  /// header's unhalved width. In a sixteen-bit film that is the size already in use and nothing
  /// changes. In a twenty-four-bit film it is a change of format, so FFmpeg drops its picture, emits a
  /// blank one, and drops it again on the next coded frame; every inter frame until the next key frame
  /// then edits a blank picture. Those pictures say nothing about the codec and are not compared.
  /// </remarks>
  private static List<(int Width, int Height, bool Comparable)> _FFmpegsPictureSizes(List<CodedPacket> packets) {
    var result = new List<(int, int, bool)>(packets.Count);
    (TrueMotion1Format.Algorithm Algorithm, int Width, int Height) state = default;
    var tainted = false;
    foreach (var packet in packets) {
      var header = TrueMotion1Format.ReadHeader(packet.Data.Span);
      var algorithm = TrueMotion1Format.CompressionTypes[header.Compression].Algorithm;
      var keyFrame = (header.EffectiveFlags & TrueMotion1Format.FlagKeyFrame) != 0;
      var next = algorithm == TrueMotion1Format.Algorithm.Rgb24
        ? (algorithm, header.XSize >> 1, header.YSize)
        : (TrueMotion1Format.Algorithm.Rgb16, header.XSize, header.YSize);

      var reset = state != default && next != state;
      state = next;
      if (algorithm == TrueMotion1Format.Algorithm.None)
        tainted |= reset;
      else if (keyFrame)
        tainted = false;
      else
        tainted |= reset;

      result.Add((next.Item2, next.Item3, !tainted));
    }
    return result;
  }

  private static bool _IsTwentyFourBit(byte[] film, MediaStreamInfo stream) {
    var first = VideoFormatRegistry.ReadPackets(film, stream.Index).First(static p => p.Data.Length != 0);
    var header = TrueMotion1Format.ReadHeader(first.Data.Span);
    return TrueMotion1Format.CompressionTypes[header.Compression].Algorithm == TrueMotion1Format.Algorithm.Rgb24;
  }

  private static byte[] _ToRgb24(ReadOnlySpan<byte> native, int bytesPerPixel) {
    var pixels = native.Length / bytesPerPixel;
    var result = new byte[pixels * 3];
    for (var i = 0; i < pixels; ++i) {
      if (bytesPerPixel == 4) {
        result[i * 3] = native[i * 4 + 2];
        result[i * 3 + 1] = native[i * 4 + 1];
        result[i * 3 + 2] = native[i * 4];
      } else {
        var word = native[i * 2] | (native[i * 2 + 1] << 8);
        int r = (word >> 10) & 31, g = (word >> 5) & 31, b = word & 31;
        result[i * 3] = (byte)((r << 3) | (r >> 2));
        result[i * 3 + 1] = (byte)((g << 3) | (g >> 2));
        result[i * 3 + 2] = (byte)((b << 3) | (b >> 2));
      }
    }
    return result;
  }

  private static int _FirstDifference(byte[] a, byte[] b) {
    for (var i = 0; i < Math.Min(a.Length, b.Length); ++i)
      if (a[i] != b[i])
        return i;
    return Math.Min(a.Length, b.Length);
  }
}
