using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class RascVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void FirstPictureIsAKbndKeyframeAndRoundTripsExactly() {
    var encoder = RascVideoEncoder.Create(_Stream(2, 2));
    var decoder = RascVideoDecoder.Create(encoder.DescribeStream());
    var source = _Rgb(2, 2,
      255, 0, 0,   0, 255, 0,
      0, 0, 255,   255, 255, 255);

    Assert.That(encoder.TryEncode(source, 17, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.Data.Span[..4].ToArray(), Is.EqualTo(new byte[] { (byte)'K', (byte)'B', (byte)'N', (byte)'D' }));
    });

    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void LaterPictureIsABndlPredictedDeltaAndRoundTripsExactly() {
    var encoder = RascVideoEncoder.Create(_Stream(3, 2));
    var decoder = RascVideoDecoder.Create(encoder.DescribeStream());
    var first = _Rgb(3, 2,
      1, 2, 3,    4, 5, 6,    7, 8, 9,
      10, 11, 12, 13, 14, 15, 16, 17, 18);
    var second = _Rgb(3, 2,
      1, 2, 3,    40, 50, 60, 7, 8, 9,
      10, 11, 12, 13, 14, 15, 160, 170, 180);

    Assert.That(encoder.TryEncode(first, 0, out var keyframe), Is.True);
    Assert.That(decoder.TryDecode(keyframe, out _), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var predicted), Is.True);

    Assert.Multiple(() => {
      Assert.That(predicted.IsKeyFrame, Is.False);
      Assert.That(predicted.Data.Span[..4].ToArray(), Is.EqualTo(new byte[] { (byte)'B', (byte)'N', (byte)'D', (byte)'L' }));
      Assert.That(predicted.Data.Span.Slice(4, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'D', (byte)'L', (byte)'T', (byte)'A' }));
    });

    Assert.That(decoder.TryDecode(predicted, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(second.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CompletelyUnchangedPredictedPictureRoundTripsThroughSkipRuns() {
    var encoder = RascVideoEncoder.Create(_Stream(4, 1));
    var decoder = RascVideoDecoder.Create(encoder.DescribeStream());
    var source = _Rgb(4, 1,
      10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120);

    encoder.TryEncode(source, 0, out var first);
    decoder.TryDecode(first, out _);
    encoder.TryEncode(source, 1, out var second);

    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void BgrInputUsesTheSharedLosslessConversionRoute() {
    var encoder = RascVideoEncoder.Create(_Stream(1, 1));
    var decoder = RascVideoDecoder.Create(encoder.DescribeStream());
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgr24,
      PixelData = [30, 20, 10],
    };

    encoder.TryEncode(source, null, out var packet);
    decoder.TryDecode(packet, out var decoded);

    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void RegistryAdvertisesBothDirections() {
    var stream = _Stream(2, 2);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.AllEncoders.Select(codec => codec.CodecName),
        Does.Contain("RemotelyAnywhere Screen Capture"));
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<RascVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void WrongGeometryRefusesInsteadOfChangingTheStreamMidFlight() {
    var encoder = RascVideoEncoder.Create(_Stream(2, 2));
    var wrong = _Rgb(1, 1, 1, 2, 3);

    Assert.Throws<System.IO.InvalidDataException>(() => encoder.TryEncode(wrong, null, out _));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RASC"),
    Width = width,
    Height = height,
    BitsPerPixel = 32,
  };

  private static RawImage _Rgb(int width, int height, params byte[] pixels) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgb24,
    PixelData = pixels,
  };
}