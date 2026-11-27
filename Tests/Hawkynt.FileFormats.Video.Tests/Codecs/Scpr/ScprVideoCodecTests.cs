using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>ScreenPressor both ways, with FFmpeg as the oracle.</summary>
[TestFixture]
public sealed class ScprVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void ScprIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<ScprVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<ScprVideoEncoder>());
    });
  }

  [TestCase(96, 64)]
  [TestCase(37, 21)]
  [Category("Unit")]
  public void APictureComesBackExactly(int width, int height) {
    var (_, ours, originals) = _Encode(width, height);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [TestCase(96, 64)]
  [TestCase(37, 21)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _) = _Encode(width, height);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "bgr24");
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  [TestCase(160, 96)]
  [TestCase(37, 21)]
  [TestCase(16, 16)]
  [Category("Unit")]
  public void AThirdGenerationPictureComesBackExactly(int width, int height) {
    var (_, ours, originals) = _EncodeThird(width, height, 8);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [TestCase(160, 96, 8)]
  [TestCase(37, 21, 8)]
  [TestCase(16, 16, 4)]
  [TestCase(560, 420, 3)]
  [Category("Conformance")]
  public void FFmpegReadsTheThirdGenerationAsThisDecoderDoes(int width, int height, int pictures) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, originals) = _EncodeThird(width, height, pictures);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "bgr24");
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i) {
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
        Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i} against its original");
      }
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>SCPR_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/SCPR.avi</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real ScreenPressor films; point SCPR_SAMPLES at them.")]
  public void EveryPictureOfARealScprFilmMatchesFFmpeg()
    => RealSampleComparison.Run("SCPR_SAMPLES", ScprVideoDecoder.Accepts, "bgr24");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("SCPR"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 10),
    FrameRate = new Rational(10, 1),
  };

  /// <summary>
  /// A third-generation film built to walk every colour model through every type: regions whose
  /// palettes hold 3, 9, 30, 90 and 256 colours, then scrolls (moved blocks, new vectors and the last
  /// one again), small edits (rectangles), an unchanged picture, and a picture changed throughout.
  /// </summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _EncodeThird(int width, int height, int pictures) {
    var encoder = ScprVideoEncoder.CreateThirdGeneration(_Stream(width, height));
    var decoder = ScprVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    var random = new Random(5);
    var bgr = new byte[width * height * 3];
    ReadOnlySpan<int> palettes = [3, 9, 30, 90, 256];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        var region = Math.Min(x * palettes.Length / width, palettes.Length - 1);
        var colours = palettes[region];
        if (y < 2) {
          // a row of all-different first components under one context, so a model sees more than 64
          // symbols before any repeats and takes the raw-byte path to its last type
          bgr[at] = (byte)(7 * x + 13 * y);
          bgr[at + 1] = 100;
          bgr[at + 2] = 50;
          continue;
        }
        if (y % 7 == 3 && region < 2) {
          bgr[at] = bgr[at + 1] = bgr[at + 2] = 240; // a line, so copies above and gradients appear
          continue;
        }
        var c = random.Next(colours);
        bgr[at] = (byte)(c * 37 + region);
        bgr[at + 1] = (byte)(colours == 256 ? random.Next(256) : c * 11);
        bgr[at + 2] = (byte)(colours == 256 ? random.Next(256) : 255 - c * 5);
      }

    for (var n = 0; n < pictures; ++n) {
      var next = (byte[])bgr.Clone();
      switch (n % 8) {
        case 1: // scroll up three rows
        case 2: // and again: the same vector
          for (var y = 0; y < height; ++y)
            Array.Copy(bgr, Math.Min(y + 3, height - 1) * width * 3, next, y * width * 3, width * 3);
          break;
        case 3: // small edits inside blocks
          for (var i = 0; i < 6; ++i) {
            int x0 = random.Next(width), y0 = random.Next(height);
            for (var y = y0; y < Math.Min(y0 + 3, height); ++y)
              for (var x = x0; x < Math.Min(x0 + 4, width); ++x)
                next[(y * width + x) * 3 + 1] ^= 0x55;
          }
          // and a patch moved two columns, which a vector inside a rectangle states
          for (var y = Math.Min(5, height - 1); y < Math.Min(9, height); ++y)
            for (var x = Math.Min(3, width - 1); x < Math.Min(9, width - 2); ++x)
              Array.Copy(bgr, (y * width + x + 2) * 3, next, (y * width + x) * 3, 3);
          break;
        case 4: // nothing changes
          break;
        case 5: // scroll left five columns
          for (var y = 0; y < height; ++y)
            for (var x = 0; x < width; ++x)
              Array.Copy(bgr, (y * width + Math.Min(x + 5, width - 1)) * 3, next, (y * width + x) * 3, 3);
          break;
        case 6: // everything changes
          random.NextBytes(next.AsSpan(0, next.Length / 2));
          break;
        case 7: // a flat window
          for (var y = height / 4; y < height / 2; ++y)
            for (var x = width / 4; x < width / 2; ++x)
              next.AsSpan((y * width + x) * 3, 3).Fill(77);
          break;
      }
      if (n > 0)
        bgr = next;
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = (byte[])bgr.Clone() };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"picture {n}");
      ours.Add(decoded.PixelData);
      originals.Add((byte[])bgr.Clone());
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), ours, originals);
  }

  /// <summary>A screen: flat panels, a gradient, noise, a window that moves, and a frame that does not change.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(int width, int height) {
    var encoder = ScprVideoEncoder.Create(_Stream(width, height));
    var decoder = ScprVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < 5; ++n) {
      var step = n == 3 ? 2 : n;
      var bgr = new byte[width * height * 3];
      var noise = new Random(11);
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = (y * width + x) * 3;
          var window = x >= step * 6 && x < step * 6 + width / 3 && y > height / 3;
          bgr[at] = (byte)(window ? 30 : y < 6 ? 200 : x * 255 / width);
          bgr[at + 1] = (byte)(window ? noise.Next(256) : 220);
          bgr[at + 2] = (byte)(window ? 90 : (x + y) & 0xF0);
        }
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = bgr };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"picture {n}");
      ours.Add(decoded.PixelData);
      originals.Add(bgr);
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), ours, originals);
  }
}
