using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Mpeg.Tests;

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
  public void BFramesDelayPacketsAndPreservePresentationAndDecodeTimelines() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 32));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < 5; ++index)
      if (encoder.TryEncode(_Picture(32, 32, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    Assert.Multiple(() => {
      Assert.That(packets.Select(static packet => packet.PresentationTimestamp),
        Is.EqualTo(new long?[] { 0, 3, 1, 2, 4 }));
      Assert.That(packets.Select(static packet => packet.DecodeTimestamp),
        Is.EqualTo(new long?[] { 0, 1, 2, 3, 4 }));
      Assert.That(packets.Select(static packet => packet.IsKeyFrame),
        Is.EqualTo(new[] { true, false, false, false, false }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AGroupContainsIForwardPredictedAndBidirectionallyPredictedPictures() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(64, 48));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < 4; ++index)
      if (encoder.TryEncode(_Picture(64, 48, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    var codingTypes = packets.ToDictionary(
      static packet => packet.PresentationTimestamp!.Value,
      static packet => _PictureCodingType(packet.Data.Span));

    Assert.Multiple(() => {
      Assert.That(codingTypes[0], Is.EqualTo(MpegPictureDecoder.IntraCoded));
      Assert.That(codingTypes[1], Is.EqualTo(MpegPictureDecoder.BidirectionallyCoded));
      Assert.That(codingTypes[2], Is.EqualTo(MpegPictureDecoder.BidirectionallyCoded));
      Assert.That(codingTypes[3], Is.EqualTo(MpegPictureDecoder.PredictiveCoded));
    });
  }

  [Test]
  [Category("Unit")]
  public void BMacroblocksChooseForwardBackwardAndBidirectionalReferences() {
    // Four solid pictures per run, so every macroblock of a B picture faces the same choice and the
    // first one speaks for all of them. Display order is I0 B1 B2 P3 and coding order I0 P3 B1 B2,
    // so B1 and B2 both see the reconstructed I0 behind them and the reconstructed P3 ahead.
    //
    //   16 128  16 240 : B2 is what lies behind it, and nothing like what lies ahead -> forward
    //   16 128 240 240 : B2 is what lies ahead of it, and nothing like what lies behind -> backward
    //   in both runs B1 is the average of the two anchors and neither one -> interpolated
    var toward = _BMacroblockTypes(16, 128, 16, 240);
    var away = _BMacroblockTypes(16, 128, 240, 240);

    Assert.Multiple(() => {
      Assert.That(toward[0] & MpegVlcTables.TypeMotionForward, Is.Not.Zero, "the averaged B picture lost its forward reference");
      Assert.That(toward[0] & MpegVlcTables.TypeMotionBackward, Is.Not.Zero, "the averaged B picture lost its backward reference");
      Assert.That(toward[1] & MpegVlcTables.TypeMotionForward, Is.Not.Zero, "a B picture matching the anchor behind it must predict forward");
      Assert.That(toward[1] & MpegVlcTables.TypeMotionBackward, Is.Zero, "a strictly better forward prediction should not spend a backward vector");
      Assert.That(away[1] & MpegVlcTables.TypeMotionBackward, Is.Not.Zero, "a B picture matching the anchor ahead of it must predict backward");
      Assert.That(away[1] & MpegVlcTables.TypeMotionForward, Is.Zero, "a strictly better backward prediction should not spend a forward vector");
    });
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesBPicturesInEveryPredictionDirection() {
    FFmpegOracle.RequireAvailable();

    // The same three runs, handed to a decoder that is not this one. A backward-only or interpolated
    // macroblock that this package writes and reads consistently but writes wrongly would agree with
    // itself forever; FFmpeg is what can disagree.
    foreach (var (name, values) in new (string Name, byte[] Values)[] {
      ("forward", [16, 128, 16, 240]),
      ("backward", [16, 128, 240, 240]),
      ("interpolated", [16, 128, 128, 240]),
    }) {
      var sources = values.Select(static value => _Solid(32, 32, value)).ToList();
      var packets = _Encode(sources);
      var decoded = _DecodeWithFFmpeg(packets, 32, 32, sources.Count);

      for (var index = 0; index < sources.Count; ++index) {
        var frameBytes = 32 * 32 * 3;
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(10d),
          $"the {name} run's picture {index} is not the frame that was encoded");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AKeyFrameBoundaryDoesNotLeaveBackwardOrderedPicturesDependingOnThePreviousGop() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(64, 48));
    var packets = new List<CodedPacket>();

    for (var index = 0; index <= 12; ++index)
      if (encoder.TryEncode(_Picture(64, 48, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    var key = packets.Single(static packet => packet.PresentationTimestamp == 12);
    var keyAt = packets.IndexOf(key);

    Assert.Multiple(() => {
      Assert.That(key.IsKeyFrame, Is.True);
      Assert.That(packets.Take(keyAt).TakeLast(2).Select(static packet => packet.PresentationTimestamp),
        Is.EqualTo(new long?[] { 10, 11 }));
      Assert.That(_PictureCodingType(packets[keyAt - 2].Data.Span), Is.EqualTo(MpegPictureDecoder.PredictiveCoded));
      Assert.That(_PictureCodingType(packets[keyAt - 1].Data.Span), Is.EqualTo(MpegPictureDecoder.PredictiveCoded));
    });

    var decoder = Mpeg2VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(key, out _), Is.False);
    Assert.That(decoder.Flush().Count(), Is.EqualTo(1), "the I packet must decode without an earlier GOP");
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
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

    Assert.That(worst, Is.LessThan(12d), "a predicted picture drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToSmallPredictedPictures() {
    const int width = 128;
    const int height = 96;
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 7; ++frame)
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    var intraBytes = packets.Single(static packet => packet.PresentationTimestamp == 0).Data.Length;
    var smallestPredicted = packets.Where(static packet => packet.PresentationTimestamp != 0).Min(static packet => packet.Data.Length);

    Assert.That(smallestPredicted, Is.LessThan(intraBytes / 2d),
      $"the smallest settled predicted picture is {smallestPredicted} bytes against {intraBytes} for the intra one");
  }

  [Test]
  [Category("RoundTrip")]
  public void PredictingMotionCostsFewerBytesThanCodingEveryPictureWhole() {
    const int width = 128;
    const int height = 96;
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 8; ++frame)
      if (encoder.TryEncode(_MovingSquare(width, height, frame), frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    var intraBytes = packets.Single(static packet => packet.PresentationTimestamp == 0).Data.Length;
    var averagePredicted = packets.Where(static packet => packet.PresentationTimestamp != 0)
      .Average(static packet => packet.Data.Length);

    Assert.That(averagePredicted, Is.LessThan(intraBytes * 0.9),
      $"a predicted picture averaged {averagePredicted:F0} bytes against {intraBytes} for the intra one");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryIPAndBPictureInDisplayOrder() {
    FFmpegOracle.RequireAvailable();

    const int width = 128;
    const int height = 96;
    const int frames = 24;

    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var frame = 0; frame < frames; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    packets.AddRange(encoder.Flush());
    Assert.That(packets.Count, Is.EqualTo(frames), "the encoder dropped a delayed picture");
    Assert.That(packets.Any(static packet => _PictureCodingType(packet.Data.Span) == MpegPictureDecoder.BidirectionallyCoded), Is.True);

    var decoded = _DecodeWithFFmpeg(packets, width, height, frames);
    var frameBytes = width * height * 3;
    for (var index = 0; index < frames; ++index) {
      var total = 0L;
      for (var offset = 0; offset < frameBytes; ++offset)
        total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

      Assert.That(total / (double)frameBytes, Is.LessThan(14d),
        $"ffmpeg's picture {index} is not the frame that was encoded");
    }
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

  /// <summary>Encodes solid pictures and reports the macroblock_type of each B picture's first macroblock.</summary>
  /// <remarks>
  /// The first macroblock of a slice is never skipped, so it is always there to be read, and over a
  /// solid picture every macroblock of the slice made the same choice it did.
  /// </remarks>
  private static int[] _BMacroblockTypes(params byte[] values) {
    var packets = _Encode(values.Select(static value => _Solid(32, 32, value)).ToList());
    var bPictures = packets
      .Where(static packet => _PictureCodingType(packet.Data.Span) == MpegPictureDecoder.BidirectionallyCoded)
      .ToArray();

    Assert.That(bPictures.Length, Is.EqualTo(2), "the group should hold two B pictures");
    return bPictures.Select(_FirstBMacroblockType).ToArray();
  }

  private static int _FirstBMacroblockType(CodedPacket packet) {
    var data = packet.Data.Span;
    var slice = _IndexOfStartCode(data, MpegStartCode.FirstSlice);
    Assert.That(slice, Is.GreaterThanOrEqualTo(0), "the B picture carries a slice");

    var reader = new MpegBitReader(data[(slice + 4)..]);
    reader.Skip(5); // quantiser_scale_code
    while (reader.NextBits(1) == 1) {
      reader.Skip(1); // extra_bit_slice
      reader.Skip(8); // extra_information_slice
    }

    reader.Skip(1); // the extra_bit_slice that says there is no more
    Assert.That(MpegVlcTables.MacroblockAddressIncrement.Read(ref reader), Is.EqualTo(1));
    return MpegVlcTables.BidirectionalMacroblockType.Read(ref reader);
  }

  private static List<CodedPacket> _Encode(IReadOnlyList<RawImage> sources) {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(sources[0].Width, sources[0].Height));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < sources.Count; ++index)
      if (encoder.TryEncode(sources[index], index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    Assert.That(packets.Count, Is.EqualTo(sources.Count), "the encoder dropped a delayed picture");
    return packets;
  }

  /// <summary>Writes the packets out as one elementary stream and returns what FFmpeg decoded.</summary>
  private static byte[] _DecodeWithFFmpeg(IReadOnlyList<CodedPacket> packets, int width, int height, int frames) {
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
      Assert.That(diagnostics.Trim(), Is.Empty, "ffmpeg read the stream but complained about it");

      var decoded = File.ReadAllBytes(raw);
      Assert.That(decoded.Length / (width * height * 3), Is.EqualTo(frames),
        "ffmpeg read a different number of pictures than were written");

      return decoded;
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static RawImage _Solid(int width, int height, byte value) {
    var data = new byte[width * height * 3];
    Array.Fill(data, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static int _PictureCodingType(ReadOnlySpan<byte> data) {
    var picture = _IndexOfStartCode(data, MpegStartCode.Picture);
    Assert.That(picture, Is.GreaterThanOrEqualTo(0), "the packet carries a picture header");
    return (data[picture + 5] >> 3) & 0x07;
  }

  private static int _IndexOfStartCode(ReadOnlySpan<byte> data, byte code) {
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
