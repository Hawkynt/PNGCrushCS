using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using Hawkynt.FileFormats.Video.Tests;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg.Tests;

[TestFixture]
public sealed class Mpeg1VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void ThreeFramesBecomeAnIntraPictureAndTwoPredictedOnes() {
    var stream = _Stream(64, 48);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 3; ++frame)
      if (encoder.TryEncode(_Gradient(64, 48, frame), frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    Assert.That(packets.Count, Is.EqualTo(3));

    // A group opens with an intra picture and continues with predicted ones. Only the first is
    // independently decodable, and IsKeyFrame has to say so or a container will index the stream
    // as though every picture were a seek point.
    Assert.That(packets.Select(static packet => packet.IsKeyFrame).ToArray(),
      Is.EqualTo(new[] { true, false, false }));
    Assert.That(packets.Select(static packet => packet.PresentationTimestamp).ToArray(),
      Is.EqualTo(new long?[] { 0, 1, 2 }));

    Assert.That(packets[0].Data.Span[..4].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceHeader }));
    Assert.That(packets[1].Data.Span[..4].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.Picture }));
    Assert.That(packets[^1].Data.Span[^4..].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceEnd }));

    var decoder = Mpeg1VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    decoded.AddRange(decoder.Flush());

    Assert.That(decoded.Count, Is.EqualTo(3));
    Assert.That(decoded.All(static frame => frame.Width == 64 && frame.Height == 48), Is.True);
    Assert.That(decoded.All(static frame => frame.Format == PixelFormat.Rgb24), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OddDimensionsAreCodedAtTheirDeclaredSizeRatherThanRoundedUp() {
    var stream = _Stream(17, 13);
    var encoder = Mpeg1VideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(_Gradient(17, 13, 0), 0, out _), Is.False);
    var packet = encoder.Flush().Single();

    var decoder = Mpeg1VideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out _), Is.False);
    var frame = decoder.Flush().Single();

    Assert.That(frame.Width, Is.EqualTo(17));
    Assert.That(frame.Height, Is.EqualTo(13));
    Assert.That(frame.PixelData.Length, Is.EqualTo(17 * 13 * 3));
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamNamesTheElementaryMpeg1PayloadWithoutPrivateContainerBytes() {
    var requested = _Stream(64, 48);
    var described = Mpeg1VideoEncoder.Create(requested).DescribeStream();

    Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("MPG1")));
    Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("MPG1")));
    Assert.That(described.CodecId, Is.EqualTo("V_MPEG1"));
    Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
    Assert.That(described.Width, Is.EqualTo(64));
    Assert.That(described.Height, Is.EqualTo(48));
    Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
  }

  [Test]
  [Category("Unit")]
  public void AFrameRateTheFourBitFieldCannotNameIsRefused() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      Width = 64,
      Height = 48,
      TimeBase = new Rational(1, 27),
      FrameRate = new Rational(27, 1),
    };

    var failure = Assert.Throws<NotSupportedException>(() => Mpeg1VideoEncoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("27/1"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureStatesItsTypeAndCarriesForwardMotion() {
    var stream = _Stream(64, 48);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 2; ++frame)
      if (encoder.TryEncode(_MovingSquare(64, 48, frame), frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    Assert.That(packets.Count, Is.EqualTo(2));

    // picture_coding_type sits in the three bits after the ten-bit temporal_reference, which
    // themselves follow the four start-code bytes.
    var second = packets[1].Data.ToArray();
    var codingType = (second[4 + 1] >> 3) & 0x07;

    Assert.Multiple(() => {
      Assert.That(second[..4],
        Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.Picture }));
      Assert.That(codingType, Is.EqualTo(MpegPictureDecoder.PredictiveCoded),
        "the second picture of a group predicts from the first");
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
    // Twelve frames is a whole group: one intra picture and eleven predicted ones, so the last
    // frame is as far from an intra picture as this encoder ever places one. Drift -- an encoder
    // predicting from its source rather than from what its decoder reconstructs -- shows up as
    // error that grows along the group, which comparing the LAST frame catches and comparing the
    // first would not.
    const int width = 64;
    const int height = 48;
    var stream = _Stream(width, height);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var sources = new List<RawImage>();
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 12; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    packets.AddRange(encoder.Flush());

    var decoder = Mpeg1VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    decoded.AddRange(decoder.Flush());

    Assert.That(decoded.Count, Is.EqualTo(sources.Count));

    var worst = 0d;
    for (var index = 0; index < decoded.Count; ++index)
      worst = Math.Max(worst, _MeanAbsoluteError(sources[index], decoded[index]));

    // MPEG-1 is lossy and this codes at a fixed quantiser, so the bar is that the picture is
    // recognisably the one that went in, not that it is identical. A drifting predictor pushes
    // this into the tens within a group.
    Assert.That(worst, Is.LessThan(12d),
      "a predicted picture drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void PredictingMotionCostsFewerBytesThanCodingEveryPictureWhole() {
    // The point of a predicted picture. A small square moving over a still background is what
    // prediction is for: the background macroblocks match the reference exactly and are skipped
    // outright, and only the few the square touches cost anything. The frame is large enough that
    // background dominates, as it does in real footage. If the encoder ever silently reverts to
    // all-intra, or stops skipping, this is what notices.
    const int width = 128;
    const int height = 96;
    var stream = _Stream(width, height);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 8; ++frame)
      if (encoder.TryEncode(_MovingSquare(width, height, frame), frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    var intraBytes = packets[0].Data.Length;
    var predictedBytes = packets.Skip(1).Sum(static packet => packet.Data.Length);
    var averagePredicted = predictedBytes / (double)(packets.Count - 1);

    Assert.That(averagePredicted, Is.LessThan(intraBytes / 2d),
      $"a predicted picture averaged {averagePredicted:F0} bytes against {intraBytes} for the intra one");
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
    for (var y = squareY; y < Math.Min(squareY + 12, height); ++y)
    for (var x = squareX; x < Math.Min(squareX + 12, width); ++x) {
      var at = (y * width + x) * 3;
      data[at] = 230;
      data[at + 1] = 200;
      data[at + 2] = 60;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryPredictedPictureAndNotOnlyTheIntraOne() {
    // The registry's oracle only asks FFmpeg for the first frame, which in a group is the intra
    // picture -- exactly the one that was already right before any of this. A malformed vector,
    // a miscounted address increment or a coded block pattern that disagrees with the blocks after
    // it would sail past that check and fail in a real player on frame two. So decode the whole
    // clip with FFmpeg and compare every frame, predicted ones included.
    FFmpegOracle.RequireAvailable();

    const int width = 128;
    const int height = 96;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var stream = _Stream(width, height);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var frame = 0; frame < frames; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    packets.AddRange(encoder.Flush());

    var directory = Directory.CreateTempSubdirectory("mpeg1-oracle");
    try {
      var clip = Path.Combine(directory.FullName, "clip.m1v");
      using (var file = File.Create(clip))
        foreach (var packet in packets)
          file.Write(packet.Data.Span);

      var raw = Path.Combine(directory.FullName, "decoded.rgb");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-i", clip, "-f", "rawvideo", "-pix_fmt", "rgb24", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      process.WaitForExit(60_000);

      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the stream: {diagnostics}");

      var decoded = File.ReadAllBytes(raw);
      var frameBytes = width * height * 3;
      Assert.That(decoded.Length / frameBytes, Is.EqualTo(frames),
        "ffmpeg read a different number of pictures than were written");

      // Every frame, not an average over the clip: drift or a broken predictor shows as one bad
      // picture among good ones, which an average hides.
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

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };

  private static RawImage _Gradient(int width, int height, int phase) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      data[at] = (byte)((x * 255 / Math.Max(1, width - 1) + phase * 17) & 0xFF);
      data[at + 1] = (byte)((y * 255 / Math.Max(1, height - 1) + phase * 29) & 0xFF);
      data[at + 2] = (byte)(((x / 5 + y / 3 + phase) & 1) == 0 ? 240 : 24);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }
}
