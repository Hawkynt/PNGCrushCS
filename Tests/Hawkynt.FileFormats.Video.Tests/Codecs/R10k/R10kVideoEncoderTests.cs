using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The r10k encoder, against the package's own decoder and the word written out by hand.
/// </summary>
[TestFixture]
public class R10kVideoEncoderTests {

  private static readonly CodecTag _R10K = CodecTag.FromCharacters("R10k");

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1001, 30000),
    FrameRate = new Rational(30000, 1001),
  };

  /// <summary>Pseudo-random ten-bit samples with the alpha field fully opaque, which is what the decoder hands back.</summary>
  private static RawImage _RandomRgb30(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      var word = (uint)random.Next(1 << 30) | 0xC0000000u;
      BitConverter.TryWriteBytes(pixels.AsSpan(i * 4), word);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb30, PixelData = pixels };
  }

  private static RawImage _RandomRgb24(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = R10kVideoEncoder.Create(_Stream(33, 25, 1));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(R10kVideoEncoder.Codec, Is.EqualTo(_R10K));
      Assert.That(described.Codec, Is.EqualTo(_R10K));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(1));
      Assert.That(described.Width, Is.EqualTo(33));
      Assert.That(described.Height, Is.EqualTo(25));
      Assert.That(described.BitsPerPixel, Is.EqualTo(32));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1001, 30000)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(described.ContainerPrivateData.IsEmpty, Is.True);
      Assert.That(R10kVideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void PacksRedHighGreenMiddleBlueLowBigEndianWithNoPadding() {
    // R=500, G=300, B=700 in Rgb30's own layout is R | G<<10 | B<<20; r10k's word is
    // R<<22 | G<<12 | B<<2 = 0x7D12CAF0, stored big-endian, and a one-pixel row is four bytes.
    var pixels = new byte[4];
    BitConverter.TryWriteBytes(pixels, 500u | (300u << 10) | (700u << 20) | 0xC0000000u);
    var frame = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb30, PixelData = pixels };
    var encoder = R10kVideoEncoder.Create(_Stream(1, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x7D, 0x12, 0xCA, 0xF0 }));
  }

  [Test]
  [Category("Unit")]
  public void ARowIsExactlyWidthTimesFourBytes() {
    Assert.That(R10kVideoEncoder.Create(_Stream(33, 2)).TryEncode(_RandomRgb30(33, 2, 1), 0, out var packet), Is.True);

    Assert.That(packet.Data.Length, Is.EqualTo(33 * 4 * 2));
  }

  [Test]
  [Category("Unit")]
  [TestCase(8, 2)]
  [TestCase(33, 25)]
  [TestCase(64, 40)]
  public void RoundTripsPseudoRandomRgb30ThroughTheRegistryDecoder(int width, int height) {
    var encoder = R10kVideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < 3; ++i) {
      var source = _RandomRgb30(width, height, 5000 + i);
      Assert.That(encoder.TryEncode(source, i, out var packet), Is.True);
      Assert.That(packet.Data.Length, Is.EqualTo(width * 4 * height));

      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb30));
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
      });
    }
  }

  [Test]
  [Category("Unit")]
  [TestCase(33, 25)]
  [TestCase(5, 3)]
  public void RoundTripsRgb24ThroughTenBitsAndBack(int width, int height) {
    var encoder = R10kVideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    var source = _RandomRgb24(width, height, 99);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.PixelData, Is.EqualTo(FastRawImageConverter.Convert(source, PixelFormat.Rgb30).PixelData));
      Assert.That(FastRawImageConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void DropsTheTwoAlphaBitsItHasNoRoomFor() {
    var pixels = new byte[4];
    BitConverter.TryWriteBytes(pixels, 500u | (300u << 10) | (700u << 20) | 0x40000000u);
    var frame = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb30, PixelData = pixels };
    var encoder = R10kVideoEncoder.Create(_Stream(1, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x7D, 0x12, 0xCA, 0xF0 }));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = R10kVideoEncoder.Create(_Stream(4, 2, 3));

    Assert.That(encoder.TryEncode(_RandomRgb30(4, 2, 1), 42, out var packet), Is.True);
    Assert.That(encoder.TryEncode(_RandomRgb30(4, 2, 2), null, out var untimed), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(untimed.PresentationTimestamp, Is.Null);
      Assert.That(untimed.DecodeTimestamp, Is.Null);
      Assert.That(untimed.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = R10kVideoEncoder.Create(_Stream(4, 2));
    Assert.That(encoder.TryEncode(_RandomRgb30(4, 2, 1), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_RandomRgb30(4, 3, 2), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("4x3"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPixelFormatItCannotCarryLosslesslyByName() {
    var encoder = R10kVideoEncoder.Create(_Stream(2, 1));
    var rgba = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgba32, PixelData = new byte[8] };
    var rgb48 = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb48, PixelData = new byte[12] };

    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgba, 0, out _))!.Message, Does.Contain("Rgba32"));
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgb48, 0, out _))!.Message, Does.Contain("Rgb48"));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithTooLittlePixelData() {
    var encoder = R10kVideoEncoder.Create(_Stream(4, 2));
    var short_ = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Rgb30, PixelData = new byte[31] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(short_, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamWithNoPixelsOrOfAnotherKind() {
    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => R10kVideoEncoder.Create(_Stream(0, 4)))!.Message, Does.Contain("0x4"));
      Assert.Throws<NotSupportedException>(() => R10kVideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 }));
    });
  }
}
