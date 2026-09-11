using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Indeo.Tests;

[TestFixture]
public sealed class Indeo4VideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 3,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IV41"),
    Handler = CodecTag.FromCharacters("IV41"),
    Width = width,
    Height = height,
    TimeBase = new(1, 25),
    FrameRate = new(25, 1),
  };

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheIndeo4Encoder() {
    var stream = _Stream(64, 48);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Indeo4VideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheStreamDescriptionNamesARealIv41VfwStream() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(73, 51));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("IV41")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("IV41")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(73));
      Assert.That(stream.Height, Is.EqualTo(51));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
    });
  }

  [TestCase(1, 1)]
  [TestCase(17, 9)]
  [TestCase(64, 48)]
  [TestCase(257, 259)]
  [Category("Unit")]
  public void WhatTheEncoderWritesTheIndeoDecoderReads(int width, int height) {
    var frame = _Picture(width, height);
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, 17, out var packet), Is.True);
    var decoded = new Indeo4Decoder().Decode(packet.Data);

    Assert.That(decoded, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(decoded!.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(decoded.Luma, Is.EqualTo(_ExpectedLuma(frame)));
    });

    var (expectedBlue, expectedRed) = _ExpectedChroma(frame);
    Assert.Multiple(() => {
      Assert.That(decoded!.ChromaBlue.Length, Is.EqualTo(expectedBlue.Length));
      Assert.That(decoded.ChromaRed.Length, Is.EqualTo(expectedRed.Length));
      Assert.That(_MaximumDifference(decoded.ChromaBlue, expectedBlue), Is.LessThanOrEqualTo(1));
      Assert.That(_MaximumDifference(decoded.ChromaRed, expectedRed), Is.LessThanOrEqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void ConsecutivePacketsRemainIndependentKeyFrames() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(33, 19));
    var first = _Picture(33, 19, seed: 1);
    var second = _Picture(33, 19, seed: 2);

    Assert.That(encoder.TryEncode(first, 0, out var a), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var b), Is.True);

    var firstDecoded = new Indeo4Decoder().Decode(a.Data);
    var secondDecoded = new Indeo4Decoder().Decode(b.Data);
    Assert.Multiple(() => {
      Assert.That(a.IsKeyFrame, Is.True);
      Assert.That(b.IsKeyFrame, Is.True);
      Assert.That(firstDecoded!.Luma, Is.EqualTo(_ExpectedLuma(first)));
      Assert.That(secondDecoded!.Luma, Is.EqualTo(_ExpectedLuma(second)));
    });
  }

  [TestCase(0, 48)]
  [TestCase(64, 0)]
  [TestCase(65536, 1)]
  [Category("Unit")]
  public void ASizeTheIv4HeaderCannotStateIsRefused(int width, int height)
    => Assert.Throws<NotSupportedException>(() => Indeo4VideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AFrameWhoseGeometryDoesNotMatchTheStreamIsRefused() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(16, 16));
    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(17, 16), null, out _));
    Assert.That(failure!.Message, Does.Contain("fixed at 16x16"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourcePixelsAreRefused() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(4, 4));
    var frame = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3 - 1],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, null, out _));
  }

  private static RawImage _Picture(int width, int height, int seed = 0) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x * 37 + y * 11 + seed * 53) & 0xFF);
        pixels[at + 1] = (byte)((x * 7 + y * 29 + seed * 31) & 0xFF);
        pixels[at + 2] = (byte)((x * 19 + y * 3 + seed * 17) & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte[] _ExpectedLuma(RawImage frame) {
    var rgb = frame.ToRgb24();
    var result = new byte[frame.Width * frame.Height];
    for (var i = 0; i < result.Length; ++i) {
      var at = i * 3;
      var r = rgb[at];
      var g = rgb[at + 1];
      var b = rgb[at + 2];
      result[i] = _Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
    }

    return result;
  }

  private static (byte[] Blue, byte[] Red) _ExpectedChroma(RawImage frame) {
    var rgb = frame.ToRgb24();
    var width = (frame.Width + 3) >> 2;
    var height = (frame.Height + 3) >> 2;
    var blue = new byte[width * height];
    var red = new byte[blue.Length];
    var blueTotals = new int[blue.Length];
    var redTotals = new int[blue.Length];
    var counts = new int[blue.Length];

    for (var y = 0; y < frame.Height; ++y)
      for (var x = 0; x < frame.Width; ++x) {
        var source = (y * frame.Width + x) * 3;
        var at = (y >> 2) * width + (x >> 2);
        var r = rgb[source];
        var g = rgb[source + 1];
        var b = rgb[source + 2];
        blueTotals[at] += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
        redTotals[at] += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        ++counts[at];
      }

    for (var i = 0; i < blue.Length; ++i) {
      blue[i] = _Clamp((blueTotals[i] + counts[i] / 2) / counts[i]);
      red[i] = _Clamp((redTotals[i] + counts[i] / 2) / counts[i]);
    }

    return (blue, red);
  }

  private static int _MaximumDifference(byte[] actual, byte[] expected)
    => actual.Zip(expected, static (a, e) => Math.Abs(a - e)).Max();

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
