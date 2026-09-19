using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class CanopusLosslessCodecTests {

  [Test]
  [Category("Unit")]
  public void RgbEncoderRoundTripsCanonicalPredictiveFrames() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(4, 2, 24));
    var pixels = new byte[] {
      0, 1, 255, 127, 128, 129, 250, 10, 20, 5, 200, 100,
      255, 254, 1, 64, 192, 32, 17, 33, 65, 240, 224, 208,
    };

    Assert.That(encoder.TryEncode(_Image(4, 2, PixelFormat.Rgb24, pixels), 17, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.Data.Length & 1, Is.Zero, "CLLC is stored as complete byte-swapped 16-bit words.");
      Assert.That(packet.Data.Span[1], Is.EqualTo(1), "The physical second byte carries RGB coding type 1 before the word swap is undone.");
    });

    var decoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void RgbCodingTypeTwoUsesTheSamePayloadSyntax() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(2, 2, 24));
    var pixels = new byte[] {
      1, 2, 3, 250, 240, 230,
      17, 34, 51, 68, 85, 102,
    };
    encoder.TryEncode(_Image(2, 2, PixelFormat.Rgb24, pixels), null, out var packet);

    var typeTwo = packet.Data.ToArray();
    typeTwo[1] = 2;
    var decoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(decoder.TryDecode(new(0, typeTwo), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ArgbEncoderRoundTripsAndSkipsColourUnderTransparentPixels() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(3, 2, 32));
    var pixels = new byte[] {
      255, 10, 20, 30, 0, 0, 0, 0, 128, 200, 150, 100,
      0, 0, 0, 0, 255, 250, 5, 125, 1, 40, 80, 120,
    };

    Assert.That(encoder.TryEncode(_Image(3, 2, PixelFormat.Argb32, pixels), 3, out var packet), Is.True);
    Assert.That(packet.Data.Span[1], Is.EqualTo(3));

    var decoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Argb32));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void ArgbEncoderRefusesHiddenColourThatTheBitstreamCannotRepresent() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(1, 1, 32));
    var frame = _Image(1, 1, PixelFormat.Argb32, [0, 1, 0, 0]);

    var exception = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(frame, null, out _));
    Assert.That(exception!.Message, Does.Contain("hidden non-zero RGB"));
  }

  [Test]
  [Category("Unit")]
  public void Yuv422RoundTripPreservesNativeSamplesInsteadOfConvertingToRgb() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(4, 2, 16));
    var samples = new byte[] {
      // Y
      16, 32, 64, 235,
      235, 200, 100, 16,
      // U
      128, 16,
      240, 128,
      // V
      128, 240,
      16, 128,
    };

    Assert.That(encoder.TryEncode(_Image(4, 2, PixelFormat.Yuv422P8, samples), 9, out var packet), Is.True);
    Assert.That(packet.Data.Span[1], Is.Zero);

    var decoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(decoded.PixelData, Is.EqualTo(samples));
    });
  }

  [Test]
  [Category("Unit")]
  public void Yuv422EncoderRefusesOddWidthAndRgbInput() {
    Assert.Throws<NotSupportedException>(() => CanopusLosslessVideoEncoder.Create(_Stream(3, 2, 16)));

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(4, 2, 16));
    var rgb = _Image(4, 2, PixelFormat.Rgb24, new byte[4 * 2 * 3]);
    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgb, null, out _));
  }

  [Test]
  [Category("Unit")]
  public void BlockedYuvStillRefusesByName() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(4, 2, 16));
    var samples = Enumerable.Range(0, 16).Select(static value => (byte)(value * 13)).ToArray();
    encoder.TryEncode(_Image(4, 2, PixelFormat.Yuv422P8, samples), null, out var packet);

    var blocked = packet.Data.ToArray();
    blocked[0] = 1; // logical byte 1 after the 16-bit word swap
    var decoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());

    var exception = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, blocked), out _));
    Assert.That(exception!.Message, Does.Contain("Blocked CLLC YUV"));
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsSelfContainedAndKeyed() {
    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(4, 2, 24));
    var first = Enumerable.Range(0, 24).Select(static value => (byte)value).ToArray();
    var second = Enumerable.Range(0, 24).Select(static value => (byte)(255 - value * 7)).ToArray();

    encoder.TryEncode(_Image(4, 2, PixelFormat.Rgb24, first), 0, out var firstPacket);
    encoder.TryEncode(_Image(4, 2, PixelFormat.Rgb24, second), 1, out var secondPacket);

    Assert.Multiple(() => {
      Assert.That(firstPacket.IsKeyFrame, Is.True);
      Assert.That(secondPacket.IsKeyFrame, Is.True);
    });

    // A fresh decoder has seen no previous frame. Decoding packet two by itself proves that CLLC has
    // no hidden forward/backward reference state despite being carried as a video codec.
    var freshDecoder = CanopusLosslessVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(freshDecoder.TryDecode(secondPacket, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(second));
  }

  [Test]
  [Category("Unit")]
  public void EncoderIsRegisteredBesideTheExistingDecoder() {
    var codecs = VideoFormatRegistry.AllCodecs.Select(static codec => codec.CodecName).ToArray();
    var encoders = VideoFormatRegistry.AllEncoders.Select(static codec => codec.CodecName).ToArray();

    Assert.Multiple(() => {
      Assert.That(codecs, Does.Contain(CanopusLosslessVideoDecoder.CodecName));
      Assert.That(encoders, Does.Contain(CanopusLosslessVideoEncoder.CodecName));
    });
  }

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CLLC"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Image(int width, int height, PixelFormat format, byte[] data) => new() {
    Width = width,
    Height = height,
    Format = format,
    PixelData = data,
  };
}
