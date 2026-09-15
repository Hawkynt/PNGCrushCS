using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>The H.263 encoder, checked against the hand-built syntax fixtures and this package's decoder.</summary>
/// <remarks>
/// The exact flat-picture test is intentionally stronger than an ordinary round trip: its expected
/// bytes come from <see cref="H263TestStream"/>, which writes the Recommendation's fields and codewords
/// directly rather than calling the encoder's helpers. FFmpeg remains the independent interoperability
/// oracle for the table transcription and packet as a whole.
/// </remarks>
[TestFixture]
public sealed class H263VideoEncoderTests {

  private const int _SUB_QCIF_MACROBLOCKS = 8 * 6;
  private const int _PICTURE_START_CODE = 1 << 5;

  [TestCase(128, 96)]
  [TestCase(176, 144)]
  [TestCase(352, 288)]
  [TestCase(704, 576)]
  [TestCase(1408, 1152)]
  [Category("Unit")]
  public void TheFiveStandardSourceFormatsAreAccepted(int width, int height) {
    var encoder = H263VideoEncoder.Create(_Stream(width, height));

    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().Width, Is.EqualTo(width));
      Assert.That(encoder.DescribeStream().Height, Is.EqualTo(height));
    });
  }

  [TestCase(64, 48)]
  [TestCase(160, 120)]
  [TestCase(320, 240)]
  [TestCase(720, 576)]
  [TestCase(0, 0)]
  [Category("Unit")]
  public void ACustomPictureSizeIsRefusedByName(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => H263VideoEncoder.Create(_Stream(width, height)));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
      Assert.That(refusal.Message, Does.Contain("extended PTYPE"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => H263VideoEncoder.Create(
      new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("H263"), Width = 176, Height = 144 }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfADifferentSizeFromTheStreamIsRefused() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(176, 144, 128, 128), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("176x144"));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureIsExactlyTheBitstreamTheRecommendationDescribes() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    Assert.That(encoder.TryEncode(_Flat(128, 96, 128, 128), 0, out var packet), Is.True);

    var expected = new H263TestStream()
      .PictureHeader(sourceFormat: 1, quantiser: 8)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, 255)
      .ToArray();

    Assert.That(packet.Data.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureComesBackExactly() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    Assert.That(encoder.TryEncode(_Flat(128, 96, 200, 128), 7, out var packet), Is.True);

    var planes = _DecodePlanes(packet, 128, 96);
    Assert.Multiple(() => {
      Assert.That(planes.Take(128 * 96).Distinct().ToArray(), Is.EqualTo(new byte[] { 200 }));
      Assert.That(planes.Skip(128 * 96).Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void AlternatingCurrentCoefficientsRoundTripThroughTheDecoder() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    var source = _Picture(128, 96);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoded = _DecodePlanes(packet, 128, 96);
    var error = _MeanSquaredError(source.PixelData, decoded);

    Assert.That(error, Is.LessThan(64d), $"the intra picture came back {error:F1} squared levels from its source");
  }

  [Test]
  [Category("Unit")]
  public void SuccessivePicturesAdvanceTheTemporalReference() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));

    for (var index = 0; index < 2; ++index) {
      encoder.TryEncode(_Flat(128, 96, 128, 128), index, out var packet);
      var reader = new H263BitReader(packet.Data.Span);
      Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE));
      var header = H263PictureHeader.Parse(ref reader);
      Assert.That(header.TemporalReference, Is.EqualTo(index), $"picture {index}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesAStreamTheDecoderAccepts() {
    var encoder = H263VideoEncoder.Create(_Stream(176, 144));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("H263")));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(0));
      Assert.That(H263VideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfTheCodec() {
    var stream = _Stream(176, 144);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<H263VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<H263VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBackAtTheEnd()
    => Assert.That(H263VideoEncoder.Create(_Stream(128, 96)).Flush(), Is.Empty);

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAnIntraPictureAndContinuesWithPredictedOnes() {
    var encoder = H263VideoEncoder.Create(_Stream(176, 144));
    var keyFrames = new List<bool>();

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_MovingSquare(176, 144, index), index, out var packet), Is.True);
      keyFrames.Add(packet.IsKeyFrame);
    }

    // PTYPE's picture coding type follows the two discriminator bits, three display flags and the
    // three-bit source format: bit 30 of the header, counting from the first bit of the PSC.
    Assert.That(keyFrames, Is.EqualTo(new[] { true, false, false }));
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
    // A whole group: one intra picture and eleven predicted ones, so the last frame is as far from an
    // intra picture as this encoder ever places one. Drift -- an encoder predicting from its source
    // rather than from what its decoder reconstructs -- grows along a group, which comparing the LAST
    // frame catches and comparing the first cannot.
    const int width = 176;
    const int height = 144;
    var stream = _Stream(width, height);
    var encoder = H263VideoEncoder.Create(stream);
    var sources = new List<RawImage>();
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 12; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    var decoder = H263VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    Assert.That(decoded.Count, Is.EqualTo(sources.Count));

    var worst = 0d;
    for (var index = 0; index < decoded.Count; ++index)
      worst = Math.Max(worst, _MeanAbsoluteError(sources[index], decoded[index]));

    // H.263 is lossy by construction and this codes at one fixed quantiser, so the bar is that the
    // picture is recognisably the one that went in. A drifting predictor pushes this into the tens.
    Assert.That(worst, Is.LessThan(14d),
      "a predicted picture drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToUncodedMacroblocks() {
    // What COD is worth, isolated from the quantiser. The first predicted picture still costs
    // something -- it corrects the intra picture's own quantisation error -- but once that correction
    // is in the reference there is nothing left to say and every macroblock should go untransmitted.
    // An encoder that stopped setting COD, or that predicted from the source instead of from the
    // reconstruction, would never converge.
    const int width = 176;
    const int height = 144;
    var encoder = H263VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var frame = 0; frame < 6; ++frame)
      if (encoder.TryEncode(picture, frame, out var packet))
        sizes.Add(packet.Data.Length);

    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 8d),
      $"a settled predicted picture is {sizes[^1]} bytes against {sizes[0]} for the intra one; "
      + $"the run was {string.Join(", ", sizes)}");
  }

  [Test]
  [Category("RoundTrip")]
  public void PredictingMotionCostsFewerBytesThanCodingEveryPictureWhole() {
    const int width = 176;
    const int height = 144;
    var encoder = H263VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 8; ++frame)
      if (encoder.TryEncode(_MovingSquare(width, height, frame), frame, out var packet))
        packets.Add(packet);

    var intraBytes = packets[0].Data.Length;
    var averagePredicted = packets.Skip(1).Sum(static packet => packet.Data.Length) / (double)(packets.Count - 1);

    Assert.That(averagePredicted, Is.LessThan(intraBytes / 2d),
      $"a predicted picture averaged {averagePredicted:F0} bytes against {intraBytes} for the intra one");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryPredictedPictureAndNotOnlyTheIntraOne() {
    // The registry's oracle asks FFmpeg for the first frame only, which in a group is the intra
    // picture -- the one that was already right before prediction existed. A vector coded against the
    // wrong predictor, a CBPY written uncomplemented or a misplaced COD bit would sail past that and
    // fail in a real player on frame two.
    FFmpegOracle.RequireAvailable();

    const int width = 176;
    const int height = 144;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var encoder = H263VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var frame = 0; frame < frames; ++frame) {
      var picture = _MovingSquare(width, height, frame);
      sources.Add(picture);
      if (encoder.TryEncode(picture, frame, out var packet))
        packets.Add(packet);
    }

    var directory = Directory.CreateTempSubdirectory("h263-oracle");
    try {
      var clip = Path.Combine(directory.FullName, "clip.h263");
      using (var file = File.Create(clip))
        foreach (var packet in packets)
          file.Write(packet.Data.Span);

      var raw = Path.Combine(directory.FullName, "decoded.rgb");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-f", "h263", "-i", clip,
        "-f", "rawvideo", "-pix_fmt", "rgb24", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      process.WaitForExit(60_000);

      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the stream: {diagnostics}");

      var decoded = File.ReadAllBytes(raw);
      var frameBytes = width * height * 3;
      Assert.That(decoded.Length / frameBytes, Is.EqualTo(frames),
        "ffmpeg read a different number of pictures than were written");

      // Every frame, not an average: a broken predictor shows up as one bad picture among good ones.
      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(14d),
          $"ffmpeg's picture {index} is not the frame that was encoded");
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

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H263"),
    Width = width,
    Height = height,
  };

  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(chrominance);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static RawImage _Picture(int width, int height) {
    var chromaWidth = width / 2;
    var chromaHeight = height / 2;
    var planes = new byte[width * height * 3 / 2];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        planes[y * width + x] = (byte)(24 + ((x * 7 + y * 11 + (x ^ y) * 3) % 208));

    for (var y = 0; y < chromaHeight; ++y)
      for (var x = 0; x < chromaWidth; ++x) {
        planes[width * height + y * chromaWidth + x] = (byte)(80 + (x * 5 + y * 3) % 96);
        planes[width * height + chromaWidth * chromaHeight + y * chromaWidth + x] =
          (byte)(80 + (x * 2 + y * 7) % 96);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static byte[] _DecodePlanes(CodedPacket packet, int width, int height) {
    var reader = new H263BitReader(packet.Data.Span);
    Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE), "picture start code");

    var header = H263PictureHeader.Parse(ref reader);
    var picture = H263PictureDecoder.BeginPicture(
      header, new(header.MacroblockWidth, header.MacroblockHeight), reference: null);
    picture.DecodePicture(ref reader);

    var lumaSamples = width * height;
    var chromaSamples = lumaSamples / 4;
    var result = new byte[lumaSamples + 2 * chromaSamples];
    Array.Copy(picture.Target.Luma, result, lumaSamples);
    Array.Copy(picture.Target.Cb, 0, result, lumaSamples, chromaSamples);
    Array.Copy(picture.Target.Cr, 0, result, lumaSamples + chromaSamples, chromaSamples);
    return result;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));

    long squared = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      squared += difference * difference;
    }

    return (double)squared / expected.Length;
  }
}
