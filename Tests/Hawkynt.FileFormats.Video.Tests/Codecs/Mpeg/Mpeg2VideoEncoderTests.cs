using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

using System.Collections.Generic;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>The MPEG-2 encoder, on streams written here and read back through the public decoder.</summary>
/// <remarks>
/// The external conformance check belongs to <c>EncoderOracleTests</c>: the encoder is marked
/// <see cref="VerifiedByAttribute"/> for ffmpeg, so that fixture writes this codec through a real
/// container and asks an independent decoder to open it. These unit tests keep the local invariants
/// cheap and exact: declared profile/level geometry, the start-code shape, registration, refusals and
/// the fact that a non-macroblock-sized picture survives this encoder and this decoder as the same
/// visible picture rather than as its padded coded dimensions.
/// </remarks>
[TestFixture]
public sealed class Mpeg2VideoEncoderTests {

  [TestCase(16, 16, 25, 1)]
  [TestCase(720, 576, 25, 1)]
  [TestCase(720, 480, 30, 1)]
  [TestCase(352, 288, 24_000, 1_001)]
  [Category("Unit")]
  public void MainProfileAtMainLevelGeometryIsAccepted(int width, int height, long rateNumerator, long rateDenominator) {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height, new(rateNumerator, rateDenominator)));
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Width, Is.EqualTo(width));
      Assert.That(described.Height, Is.EqualTo(height));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(rateNumerator, rateDenominator)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
    });
  }

  [TestCase(721, 576, 25, 1)]
  [TestCase(720, 577, 25, 1)]
  [TestCase(720, 576, 30, 1)]
  [TestCase(17, 16, 25, 1)]
  [TestCase(16, 17, 25, 1)]
  [TestCase(0, 16, 25, 1)]
  [Category("Unit")]
  public void GeometryOutsideTheDeclaredLevelOrFourTwoZeroGridIsRefused(
    int width, int height, long rateNumerator, long rateDenominator) {
    var refusal = Assert.Throws<NotSupportedException>(
      () => Mpeg2VideoEncoder.Create(_Stream(width, height, new(rateNumerator, rateDenominator))));

    Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
  }

  [TestCase(50, 1)]
  [TestCase(60_000, 1_001)]
  [TestCase(15, 1)]
  [Category("Unit")]
  public void FrameRatesThisMainLevelWriterDoesNotSignalAreRefused(long numerator, long denominator) {
    var refusal = Assert.Throws<NotSupportedException>(
      () => Mpeg2VideoEncoder.Create(_Stream(352, 288, new(numerator, denominator))));

    Assert.That(refusal!.Message, Does.Contain($"{numerator}/{denominator}"));
  }

  [Test]
  [Category("Unit")]
  public void ThePacketCarriesTheHeadersThatMakeItMpeg2AndIndependentlyDecodable() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 16));
    Assert.That(encoder.TryEncode(_Picture(32, 16), 7, out var packet), Is.True);

    // A span cannot be captured by the Assert.Multiple lambda, so the reads happen first.
    var data = packet.Data.ToArray();
    var opening = data[..4];
    var hasExtension = _ContainsStartCode(data, MpegStartCode.Extension);
    var hasPicture = _ContainsStartCode(data, MpegStartCode.Picture);
    var hasSlice = _ContainsStartCode(data, MpegStartCode.FirstSlice);

    Assert.Multiple(() => {
      Assert.That(opening, Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceHeader }));
      Assert.That(hasExtension, Is.True);
      Assert.That(hasPicture, Is.True);
      Assert.That(hasSlice, Is.True);
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVisibleSizeThatIsNotWholeMacroblocksRoundTripsAtItsDeclaredSize() {
    const int width = 18;
    const int height = 34;

    var source = _Picture(width, height);
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var decoder = Mpeg2VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out _), Is.False, "the first I picture is the anchor held until flush");
    var decoded = decoder.Flush().Single();

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(_MeanSquaredError(source.PixelData, decoded.PixelData), Is.LessThan(400d));
    });
  }

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAKeyFrameAndNothingIsHeldByTheEncoder() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 32));
    var keyFrames = new List<bool>();

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_Picture(32, 32, index), index, out var packet), Is.True);
      keyFrames.Add(packet.IsKeyFrame);
    }

    // Only the picture that opens a group is independently decodable. IsKeyFrame has to say so, or
    // a container will index every picture in the stream as a seek point.
    Assert.That(keyFrames, Is.EqualTo(new[] { true, false, false }));
    Assert.That(encoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureStatesItsTypeAndTheForwardRangeItUses() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(64, 48));
    Assert.That(encoder.TryEncode(_Picture(64, 48, 0), 0, out _), Is.True);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 1), 1, out var predicted), Is.True);

    var data = predicted.Data.ToArray();
    var picture = _IndexOfStartCode(data, MpegStartCode.Picture);
    Assert.That(picture, Is.GreaterThanOrEqualTo(0), "the packet carries a picture header");

    // picture_coding_type occupies the three bits after the ten-bit temporal_reference.
    var codingType = (data[picture + 4 + 1] >> 3) & 0x07;
    Assert.That(codingType, Is.EqualTo(2), "the second picture of a group is predictive-coded");
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
    // A whole group: one intra picture and eleven predicted ones, so the last frame is as far from
    // an intra picture as this encoder ever places one. Drift -- an encoder predicting from its
    // source rather than from what its decoder reconstructs -- grows along a group, which comparing
    // the LAST frame catches and comparing the first cannot.
    const int width = 128;
    const int height = 96;
    var stream = _Stream(width, height);
    var encoder = Mpeg2VideoEncoder.Create(stream);
    var sources = new List<RawImage>();
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 12; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    packets.AddRange(encoder.Flush());

    var decoder = Mpeg2VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    decoded.AddRange(decoder.Flush());
    Assert.That(decoded.Count, Is.EqualTo(sources.Count));

    var worst = 0d;
    for (var index = 0; index < decoded.Count; ++index)
      worst = Math.Max(worst, _MeanAbsoluteError(sources[index], decoded[index]));

    // H.262 is lossy and this codes at a fixed quantiser, so the bar is that the picture is
    // recognisably the one that went in. A drifting predictor pushes this into the tens.
    Assert.That(worst, Is.LessThan(12d),
      "a predicted picture drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToSkippedMacroblocks() {
    // What macroblock skipping is worth, isolated from the quantiser. The first predicted picture
    // still costs something -- it corrects the intra picture's own quantisation error -- but once
    // that correction is in the reference there is nothing left to say, and every macroblock but the
    // two a slice must always code should go unwritten. An encoder that stopped skipping, or that
    // predicted from the source instead of from the reconstruction, would never converge.
    const int width = 128;
    const int height = 96;
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var frame = 0; frame < 6; ++frame)
      if (encoder.TryEncode(picture, frame, out var packet))
        sizes.Add(packet.Data.Length);

    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 4d),
      $"a settled predicted picture is {sizes[^1]} bytes against {sizes[0]} for the intra one; "
      + $"the run was {string.Join(", ", sizes)}");
  }

  [Test]
  [Category("RoundTrip")]
  public void PredictingMotionCostsFewerBytesThanCodingEveryPictureWhole() {
    // The point of a predicted picture: a small square moving over a still background costs less
    // than coding the whole picture again. The margin is modest and deliberately so -- this encoder
    // quantises a residual as finely as it quantises an intra picture, so a predicted picture buys
    // its saving by not restating the background rather than by coding what it does state coarsely.
    // If the encoder ever silently reverts to all-intra, this is what notices.
    const int width = 128;
    const int height = 96;
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 8; ++frame)
      if (encoder.TryEncode(_MovingSquare(width, height, frame), frame, out var packet))
        packets.Add(packet);

    var intraBytes = packets[0].Data.Length;
    var averagePredicted = packets.Skip(1).Sum(static packet => packet.Data.Length) / (double)(packets.Count - 1);

    Assert.That(averagePredicted, Is.LessThan(intraBytes * 0.8),
      $"a predicted picture averaged {averagePredicted:F0} bytes against {intraBytes} for the intra one");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryPredictedPictureAndNotOnlyTheIntraOne() {
    // The registry's oracle asks FFmpeg for the first frame only, which in a group is the intra
    // picture -- the one that was already right before any prediction existed. A malformed vector, a
    // miscounted address increment or a coded block pattern that disagrees with the blocks behind it
    // would sail past that and fail in a real player on frame two. So decode the whole clip.
    FFmpegOracle.RequireAvailable();

    const int width = 128;
    const int height = 96;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var frame = 0; frame < frames; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    var directory = Directory.CreateTempSubdirectory("mpeg2-oracle");
    try {
      var clip = Path.Combine(directory.FullName, "clip.m2v");
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

      // Every frame, not an average: drift or a broken predictor shows up as one bad picture among
      // good ones, which an average hides.
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

  private static int _IndexOfStartCode(byte[] data, byte code) {
    for (var index = 0; index + 3 < data.Length; ++index)
      if (data[index] == 0x00 && data[index + 1] == 0x00 && data[index + 2] == 0x01 && data[index + 3] == code)
        return index;

    return -1;
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

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefused() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 32));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(34, 32), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("34x32"));
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescriptionIsAcceptedByTheDecoderAndTheRegistryReachesBoth() {
    var stream = _Stream(32, 32);
    var described = Mpeg2VideoEncoder.Create(stream).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("MPG2")));
      Assert.That(described.CodecId, Is.EqualTo("V_MPEG2"));
      Assert.That(Mpeg2VideoDecoder.Accepts(described), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(described), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(described), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(described), Is.InstanceOf<Mpeg2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(described), Is.InstanceOf<Mpeg2VideoEncoder>());
    });
  }

  private static MediaStreamInfo _Stream(int width, int height, Rational? frameRate = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MPG2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = frameRate ?? new Rational(25, 1),
  };

  private static RawImage _Picture(int width, int height, int phase = 0) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x * 7 + phase * 13) & 0xFF);
        pixels[at + 1] = (byte)((y * 9 + phase * 5) & 0xFF);
        pixels[at + 2] = (byte)(((x + y) * 5 + phase * 17) & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static bool _ContainsStartCode(ReadOnlySpan<byte> data, byte code) {
    for (var offset = 0; offset + 3 < data.Length; ++offset)
      if (data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 1 && data[offset + 3] == code)
        return true;

    return false;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));

    double sum = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = expected[i] - actual[i];
      sum += delta * delta;
    }

    return sum / expected.Length;
  }
}
