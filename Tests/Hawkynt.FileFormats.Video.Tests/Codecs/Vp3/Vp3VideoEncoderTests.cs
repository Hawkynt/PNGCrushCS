using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>The VP3.1 writer and the VP3.0 header normalization at its decode boundary.</summary>
[TestFixture]
public sealed class Vp3VideoEncoderTests {
  private static readonly CodecTag _Vp31 = CodecTag.FromCharacters("VP31");

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAKeyFrameAndContinuesWithInterFrames() {
    var encoder = Vp3VideoEncoder.Create(_Stream(128, 96));
    var keyFrames = new List<bool>();
    var types = new List<int>();

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_MovingSquare(128, 96, index), index, out var packet), Is.True);
      keyFrames.Add(packet.IsKeyFrame);

      // The first bit of a VP3 frame is its type: zero for an intra frame, one for an inter frame.
      types.Add((packet.Data.Span[0] >> 7) & 1);
    }

    Assert.Multiple(() => {
      Assert.That(keyFrames, Is.EqualTo(new[] { true, false, false }));
      Assert.That(types, Is.EqualTo(new[] { 0, 1, 1 }), "the frame-type bit follows the key-frame flag");
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
    // A whole group: one intra frame and eleven inter frames, so the last frame is as far from a key
    // frame as this encoder ever places one. Drift -- an encoder predicting from its source rather
    // than from what its decoder reconstructs -- grows along a group, which comparing the LAST frame
    // catches and comparing the first cannot.
    const int width = 128;
    const int height = 96;
    var stream = _Stream(width, height);
    var encoder = Vp3VideoEncoder.Create(stream);
    var sources = new List<RawImage>();
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 12; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    var decoder = Vp3VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    Assert.That(decoded.Count, Is.EqualTo(sources.Count));

    var worst = 0d;
    for (var index = 0; index < decoded.Count; ++index)
      worst = Math.Max(worst, _MeanAbsoluteError(sources[index], decoded[index]));

    Assert.That(worst, Is.LessThan(16d),
      "an inter frame drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToUncodedSuperBlocks() {
    // What the coded-block flags are worth. The first inter frame still costs something -- it corrects
    // the key frame's own quantisation error -- but once that correction is in the reference there is
    // nothing left to say and no super block is coded at all. An encoder that predicted from the
    // source instead of from the reconstruction would never converge.
    const int width = 128;
    const int height = 96;
    var encoder = Vp3VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var frame = 0; frame < 6; ++frame)
      if (encoder.TryEncode(picture, frame, out var packet))
        sizes.Add(packet.Data.Length);

    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 8d),
      $"a settled inter frame is {sizes[^1]} bytes against {sizes[0]} for the key frame; "
      + $"the run was {string.Join(", ", sizes)}");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryInterFrameAndNotOnlyTheKeyFrame() {
    // The registry's oracle asks FFmpeg for the first frame only, which is the key frame -- the one
    // that was already right before inter frames existed. A miscounted coded-block run, a mode
    // written for a macro block that carries none, or a vector in the wrong units would sail past
    // that and fail in a real player on frame two.
    FFmpegOracle.RequireAvailable();

    const int width = 128;
    const int height = 96;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var stream = _Stream(width, height);
    var encoder = Vp3VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var frame = 0; frame < frames; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    var directory = Directory.CreateTempSubdirectory("vp3-oracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.avi");
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));

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
        "ffmpeg read a different number of frames than were written");

      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(16d),
          $"ffmpeg's frame {index} is not the frame that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static double _MeanAbsoluteError(RawImage expected, RawImage actual) {
    var left = expected.PixelData;
    var right = actual.PixelData;
    var total = 0L;
    var count = Math.Min(left.Length, right.Length);
    for (var index = 0; index < count; ++index)
      total += Math.Abs(left[index] - right[index]);

    return total / (double)count;
  }

  /// <summary>A bright square crossing a fixed background, which is motion and nothing else.</summary>
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
    var squareY = 8 + phase;
    for (var y = squareY; y < Math.Min(squareY + 16, height); ++y)
    for (var x = squareX; x < Math.Min(squareX + 16, width); ++x) {
      var at = (y * width + x) * 3;
      data[at] = 230;
      data[at + 1] = 200;
      data[at + 2] = 60;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static MediaStreamInfo _Stream(int width, int height, string code = "VP31", int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _GreyRamp(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)((x * 17 + y * 11) & 0xFF);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheVp31EncoderAndItsDescriptionRoundTripsToTheDecoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(31, 19, index: 7));
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(encoder, Is.InstanceOf<Vp3VideoEncoder>());
      Assert.That(Vp3VideoEncoder.Codec, Is.EqualTo(_Vp31));
      Assert.That(described.Codec, Is.EqualTo(_Vp31));
      Assert.That(described.Handler, Is.EqualTo(_Vp31));
      Assert.That(described.Index, Is.EqualTo(7));
      Assert.That(described.Width, Is.EqualTo(31));
      Assert.That(described.Height, Is.EqualTo(19));
      Assert.That(Vp3VideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [TestCase(16, 16)]
  [TestCase(31, 19)]
  [TestCase(33, 17)]
  [Category("Unit")]
  public void EncodesARealVp31PacketAndTheDecoderReadsIt(int width, int height) {
    var encoder = Vp3VideoEncoder.Create(_Stream(width, height));
    var source = _GreyRamp(width, height);

    Assert.That(encoder.TryEncode(source, 42, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.GreaterThan(3));
    Assert.That(packet.Data.Span[0], Is.EqualTo(0x3F)); // intra + finest quantiser
    Assert.That(packet.Data.Span[1], Is.EqualTo(0x00)); // width/height codes
    Assert.That(packet.Data.Span[2], Is.EqualTo(0x08)); // VP3.1, normal coding type, reserved zero
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
    Assert.That(packet.IsKeyFrame, Is.True);

    var decoder = Vp3VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(width * height * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Vp30KeyFrameIsTheSamePayloadWithTheVp31VersionByteAbsent() {
    const int width = 32;
    const int height = 16;
    var encoder = Vp3VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(_GreyRamp(width, height), 0, out var vp31), Is.True);

    var vp30Bytes = new byte[vp31.Data.Length - 1];
    vp31.Data.Span[..2].CopyTo(vp30Bytes);
    vp31.Data.Span[3..].CopyTo(vp30Bytes.AsSpan(2));

    var vp31Decoder = Vp3VideoDecoder.Create(_Stream(width, height, "VP31"));
    var vp30Decoder = Vp3VideoDecoder.Create(_Stream(width, height, "VP30"));
    Assert.That(vp31Decoder.TryDecode(vp31, out var one), Is.True);
    Assert.That(vp30Decoder.TryDecode(new(0, vp30Bytes, IsKeyFrame: true), out var zero), Is.True);

    Assert.That(zero.PixelData, Is.EqualTo(one.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RefusesWrongGeometryAndNonVideoStreams() {
    var encoder = Vp3VideoEncoder.Create(_Stream(16, 16));
    Assert.That(
      () => encoder.TryEncode(_GreyRamp(17, 16), 0, out _),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("16x16"));

    Assert.Throws<NotSupportedException>(() => Vp3VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = _Vp31,
      Width = 16,
      Height = 16,
    }));
  }
}
