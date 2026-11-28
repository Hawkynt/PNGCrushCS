using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Tmv;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>8088flex TMV: the container both ways, the codec both ways, and FFmpeg as the oracle for each.</summary>
[TestFixture]
public sealed class TmvVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void TmavIsDecodedAndEncoded() {
    var stream = _Stream(320, 200);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<TmvVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<TmvVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureDrawnFromTheFontComesBackExactly() {
    var screen = new byte[40 * 25 * 2];
    var random = new Random(7);
    random.NextBytes(screen);
    var decoder = TmvVideoDecoder.Create(_Stream(320, 200));
    decoder.TryDecode(new(0, screen), out var drawn);

    var encoder = TmvVideoEncoder.Create(_Stream(320, 200));
    encoder.TryEncode(drawn, 0, out var packet);
    decoder.TryDecode(packet, out var again);
    Assert.That(again.ToRgb24(), Is.EqualTo(drawn.ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void TheContainerRoundTripsScreensAndSound() {
    var video = _Stream(80, 48);
    var audio = new MediaStreamInfo { Index = 1, Kind = MediaStreamKind.Audio, Codec = new(1), SampleRate = 11025, Channels = 1, BitsPerSample = 8 };
    var packets = new List<CodedPacket>();
    for (var i = 0; i < 5; ++i) {
      packets.Add(new(0, Enumerable.Range(0, 10 * 6 * 2).Select(b => (byte)(b + i)).ToArray()));
      packets.Add(new(1, Enumerable.Range(0, 441).Select(b => (byte)(128 + (b % 7))).ToArray()));
    }

    var bytes = VideoIO.Mux<TmvWriter>([video, audio], packets);
    var container = TmvContainer.FromBytes(bytes);
    var streams = TmvContainer.Streams(container);
    var read = TmvContainer.ReadPackets(container).ToList();
    Assert.Multiple(() => {
      Assert.That(TmvContainer.MatchesSignature(bytes), Is.True);
      Assert.That((streams[0].Width, streams[0].Height), Is.EqualTo((80, 48)));
      Assert.That(streams[1].SampleRate, Is.EqualTo(11025));
      Assert.That(streams[0].FrameRate.Numerator * 441, Is.EqualTo(11025 * streams[0].FrameRate.Denominator));
      Assert.That(read.Select(static p => p.Data.ToArray()), Is.EqualTo(packets.Select(static p => p.Data.ToArray())));
    });
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheContainerAndEncoderWriteAsThisDecoderDoes() {
    FFmpegOracle.RequireAvailable();
    const int width = 160, height = 96;
    var encoder = TmvVideoEncoder.Create(_Stream(width, height));
    var decoder = TmvVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 6; ++i) {
      encoder.TryEncode(_Gradient(width, height, i), i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.ToRgb24());
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tmv");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<TmvWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "rgb24");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>TMV_SAMPLES</c> at a directory of real TMV films, such as FATE's <c>tmv/pop-partial.tmv</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs a directory of real TMV films; point TMV_SAMPLES at one.")]
  public void EveryPictureOfARealTmvFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var directory = Environment.GetEnvironmentVariable("TMV_SAMPLES");
    if (string.IsNullOrWhiteSpace(directory))
      Assert.Inconclusive("Set TMV_SAMPLES to a directory of real TMV films.");
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!, "*.tmv")) {
      var container = TmvContainer.FromBytes(File.ReadAllBytes(film));
      var stream = TmvContainer.Streams(container)[0];
      var decoder = TmvVideoDecoder.Create(stream);
      var ours = new List<byte[]>();
      foreach (var packet in TmvContainer.ReadPackets(container).Where(static p => p.StreamIndex == 0))
        if (packet.Data.Length == stream.Width / 8 * (stream.Height / 8) * 2 && decoder.TryDecode(packet, out var picture))
          ours.Add(picture.ToRgb24());

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, "rgb24", tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var size = stream.Width * stream.Height * 3;
      var compared = Math.Min(ours.Count, samples.Length / size);
      for (var i = 0; i < compared; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"{Path.GetFileName(film)} picture {i}");
      TestContext.Out.WriteLine($"{Path.GetFileName(film)}: {compared} of {ours.Count} pictures identical to ffmpeg");
      total += compared;
    }
    Assert.That(total, Is.GreaterThan(0));
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("TMAV"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Gradient(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x + step * 4) * 255 / width);
        pixels[at + 1] = (byte)(y * 255 / height);
        pixels[at + 2] = (byte)((x ^ y) & 0xC0);
      }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
