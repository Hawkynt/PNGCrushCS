using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.RealVideo.Tests;

[TestFixture]
public sealed class RealVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void AFlatPictureRoundTripsExactly() {
    var stream = _Stream(176, 144);
    var encoder = RealVideoEncoder.Create(stream);
    var decoder = RealVideoDecoder.Create(encoder.DescribeStream());
    var source = _Flat(176, 144, 99, 128);

    Assert.That(encoder.TryEncode(source, 7, out var packet), Is.True);
    var planes = decoder.DecodePlanes(packet);

    Assert.Multiple(() => {
      Assert.That(planes.Luma.Distinct().ToArray(), Is.EqualTo(new byte[] { 99 }));
      Assert.That(planes.Cb.Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(planes.Cr.Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.FragmentOffsets, Is.EqualTo(new[] { 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ATexturedPictureUsesTheCoefficientLayerAndComesBackClose() {
    const int width = 176;
    const int height = 144;
    var source = _Textured(width, height);
    var encoder = RealVideoEncoder.Create(_Stream(width, height));
    var decoder = RealVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoded = decoder.DecodePlanes(packet);
    var error = _MeanSquaredError(source.PixelData.AsSpan(0, width * height), decoded.Luma);

    Assert.That(error, Is.LessThan(40d));
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesRevisionZeroRv10() {
    var stream = RealVideoEncoder.Create(_Stream(352, 288)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("RV10")));
      Assert.That(stream.CodecId, Is.EqualTo("V_REAL/RV10"));
      Assert.That(stream.Width, Is.EqualTo(352));
      Assert.That(stream.Height, Is.EqualTo(288));
      Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 8, 0x10, 0, 0, 0 }));
      Assert.That(RealVideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesTheWriter() {
    var stream = _Stream(176, 144);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<RealVideoEncoder>());
    });
  }

  [TestCase(175, 144)]
  [TestCase(176, 143)]
  [TestCase(0, 0)]
  [TestCase(1024, 1024)]
  [Category("Unit")]
  public void GeometryTheSingleRunSyntaxCannotWriteIsRefused(int width, int height)
    => Assert.Throws<NotSupportedException>(() => RealVideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AFrameWhoseSizeChangesIsRefused() {
    var encoder = RealVideoEncoder.Create(_Stream(176, 144));
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(352, 288, 99, 128), 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack()
    => Assert.That(RealVideoEncoder.Create(_Stream(176, 144)).Flush(), Is.Empty);

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAnIntraPictureAndContinuesWithPredictedOnes() {
    var encoder = RealVideoEncoder.Create(_Stream(176, 144));
    var keyFrames = new List<bool>();
    var types = new List<int>();

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_MovingSquare(176, 144, index), index, out var packet), Is.True);
      keyFrames.Add(packet.IsKeyFrame);

      // The picture header opens with a marker bit and then the picture type: zero is intra.
      types.Add((packet.Data.Span[0] >> 6) & 1);
    }

    Assert.Multiple(() => {
      Assert.That(keyFrames, Is.EqualTo(new[] { true, false, false }));
      Assert.That(types, Is.EqualTo(new[] { 0, 1, 1 }), "the picture type follows the marker bit");
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesPredictionWithoutDrifting() {
    // A whole group: one intra picture and eleven predicted ones, so the last picture is as far from
    // an intra one as this encoder ever places it. Drift -- predicting from the source rather than
    // from what the decoder reconstructs -- grows along a group, which comparing the LAST picture
    // catches and comparing the first cannot.
    const int width = 176;
    const int height = 144;
    var stream = _Stream(width, height);
    var encoder = RealVideoEncoder.Create(stream);
    var decoder = RealVideoDecoder.Create(encoder.DescribeStream());

    var worst = 0d;
    for (var index = 0; index < 12; ++index) {
      var source = _MovingSquare(width, height, index);
      Assert.That(encoder.TryEncode(source, index, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      worst = Math.Max(worst, _MeanAbsoluteError(source, decoded));
    }

    Assert.That(worst, Is.LessThan(14d), "a predicted picture drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToUncodedMacroblocks() {
    // What COD is worth. The first predicted picture still corrects the intra picture's own
    // quantisation error, but once that correction is in the reference there is nothing left to say.
    const int width = 176;
    const int height = 144;
    var encoder = RealVideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var index = 0; index < 6; ++index)
      if (encoder.TryEncode(picture, index, out var packet))
        sizes.Add(packet.Data.Length);

    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 8d),
      $"a settled predicted picture is {sizes[^1]} bytes against {sizes[0]} for the intra one; "
      + $"the run was {string.Join(", ", sizes)}");
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
    Codec = CodecTag.FromCharacters("RV10"),
    Width = width,
    Height = height,
    TimeBase = new(1, 1000),
  };

  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(chrominance);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static RawImage _Textured(int width, int height) {
    var luma = width * height;
    var chroma = luma / 4;
    var planes = new byte[luma + 2 * chroma];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        planes[y * width + x] = (byte)(32 + ((x * 5 + y * 3 + (x ^ y)) % 192));

    planes.AsSpan(luma, chroma).Fill(104);
    planes.AsSpan(luma + chroma, chroma).Fill(152);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    double sum = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      sum += difference * difference;
    }

    return sum / expected.Length;
  }
}