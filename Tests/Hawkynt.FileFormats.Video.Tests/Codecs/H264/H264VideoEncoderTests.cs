using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.H264Video;
using FileFormat.Matroska;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264VideoEncoderTests {

  [Test]
  [Category("Oracle")]
  public void FFmpegReadsEveryPictureBackAsTheFrameThatWentIn() {
    // Every picture here is an independent IDR made of I_PCM macroblocks, so what a multi-picture clip
    // tests that a single picture does not is the packaging: the parameter sets, the length-prefixed
    // samples, and that picture two is where the container says it is. I_PCM stores samples verbatim,
    // so the only difference a correct decode can show is the 4:2:0 conversion the source went
    // through -- which makes the comparison tight rather than nominal.
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frames = 6;

    var encoder = H264VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var picture = _MovingSquare(width, height, index);
      sources.Add(picture);
      if (encoder.TryEncode(picture, index, out var packet))
        packets.Add(packet);
    }

    Assert.That(packets, Has.Count.EqualTo(frames));

    var directory = Directory.CreateTempSubdirectory("h264-oracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.mp4");
      File.WriteAllBytes(path, VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets));

      var raw = Path.Combine(directory.FullName, "decoded.rgb");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      process.WaitForExit(60_000);

      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the stream: {diagnostics}");

      var decoded = File.ReadAllBytes(raw);
      var frameBytes = width * height * 3;
      Assert.That(decoded.Length / frameBytes, Is.EqualTo(frames),
        "ffmpeg read a different number of pictures than were written");

      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(12d),
          $"ffmpeg's picture {index} is not the frame that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>A bright square crossing a fixed background.</summary>
  private static RawImage _MovingSquare(int width, int height, int phase) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var background = (byte)(40 + ((x / 8 + y / 8) & 1) * 30);
      data[at] = background;
      data[at + 1] = background;
      data[at + 2] = background;
    }

    var squareX = 4 + phase * 3;
    var squareY = 4 + phase;
    for (var y = squareY; y < Math.Min(squareY + 16, height); ++y)
    for (var x = squareX; x < Math.Min(squareX + 16, width); ++x) {
      var at = (y * width + x) * 3;
      data[at] = 230;
      data[at + 1] = 200;
      data[at + 2] = 60;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }


  [Test]
  [Category("Unit")]
  public void EncoderWritesLengthPrefixedIdrThatOwnDecoderReadsExactly() {
    const int width = 18;
    const int height = 20;
    var frame = _Random420(width, height, 0x264);
    var encoder = H264VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Data.Span[..4].ToArray(), Is.Not.EqualTo(new byte[] { 0, 0, 0, 1 }),
        "the codec emits AVC length-prefixed samples rather than Annex B");
    });

    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawWriterTurnsEncoderOutputIntoAnnexBWithParameterSets() {
    var frame = _Random420(32, 16, 42);
    var encoder = H264VideoEncoder.Create(_Stream(frame.Width, frame.Height));
    encoder.TryEncode(frame, 0, out var packet);

    var annexB = VideoIO.Mux<H264VideoWriter>([encoder.DescribeStream()], [packet]);
    var units = H264NalReader.SplitAnnexB(annexB).ToArray();

    Assert.That(units.Select(unit => unit.Type), Is.EqualTo(new[] {
      H264NalUnitType.SequenceParameterSet,
      H264NalUnitType.PictureParameterSet,
      H264NalUnitType.IdrSlice,
    }));

    var decoder = H264VideoDecoder.Create(_Stream(frame.Width, frame.Height));
    Assert.That(decoder.TryDecode(new(0, annexB), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [TestCase(17, 16)]
  [TestCase(16, 17)]
  [Category("Unit")]
  public void Odd420DimensionsAreRefused(int width, int height)
    => Assert.That(
      () => H264VideoEncoder.Create(_Stream(width, height)),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("Both dimensions must be even"));

  [Test]
  [Category("Unit")]
  public void RegistryExposesH264Encoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(16, 16));
    Assert.That(encoder, Is.TypeOf<H264VideoEncoder>());
  }

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      Width = width,
      Height = height,
      BitsPerPixel = 12,
    };

  private static RawImage _Random420(int width, int height, int seed) {
    var pixels = new byte[width * height * 3 / 2];
    new Random(seed).NextBytes(pixels);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }
}
