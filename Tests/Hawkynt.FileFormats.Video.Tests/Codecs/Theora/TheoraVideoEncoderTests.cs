using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Theora.Tests;

/// <summary>The Theora encoder, checked through the public decoder seam.</summary>
[TestFixture]
public sealed class TheoraVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void AFlatPictureRoundTripsThroughThePublicDecoder() {
    var encoder = TheoraVideoEncoder.Create(_Stream(16, 16));
    var decoder = TheoraVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(_Flat(16, 16, 128, 128), 7, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(16));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void APartialMacroblockIsPaddedButThePictureIsCroppedBack() {
    var encoder = TheoraVideoEncoder.Create(_Stream(17, 9));
    var decoder = TheoraVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(_Flat(17, 9, 128, 128), 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(17));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(17 * 9 * 3));
      Assert.That(decoded.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TopAndBottomRowsStayTheRightWayUp() {
    const int width = 16;
    const int height = 16;
    var planes = new byte[width * height * 3];
    var luma = planes.AsSpan(0, width * height);
    luma[..(width * 8)].Fill(200);
    luma[(width * 8)..].Fill(50);
    planes.AsSpan(width * height).Fill(128);

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = planes };
    var encoder = TheoraVideoEncoder.Create(_Stream(width, height));
    var decoder = TheoraVideoDecoder.Create(encoder.DescribeStream());

    encoder.TryEncode(source, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);

    var top = decoded.PixelData[0];
    var bottom = decoded.PixelData[(height - 1) * width * 3];
    Assert.That(top, Is.GreaterThan(bottom));
  }

  [Test]
  [Category("Unit")]
  public void EveryPictureIsAnIndependentKeyFrame() {
    var encoder = TheoraVideoEncoder.Create(_Stream(16, 16));

    encoder.TryEncode(_Flat(16, 16, 80, 128), 0, out var first);
    encoder.TryEncode(_Flat(16, 16, 180, 128), 1, out var second);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.True);
      Assert.That(first.Data, Is.Not.EqualTo(second.Data));
      Assert.That(encoder.Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesTheoraHeadersTheDecoderAccepts() {
    var stream = TheoraVideoEncoder.Create(_Stream(20, 12)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("Theo")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("Theo")));
      Assert.That(stream.CodecId, Is.EqualTo("V_THEORA"));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(42));
      Assert.That(TheoraVideoDecoder.Accepts(stream), Is.True);
      Assert.DoesNotThrow(() => TheoraVideoDecoder.Create(stream));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfTheCodec() {
    var stream = _Stream(16, 16);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(
        TheoraVideoEncoder.Create(stream).DescribeStream()), Is.InstanceOf<TheoraVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<TheoraVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AFrameWithDifferentGeometryIsRefusedByName() {
    var encoder = TheoraVideoEncoder.Create(_Stream(16, 16));
    var refusal = Assert.Throws<InvalidDataException>(() =>
      encoder.TryEncode(_Flat(17, 16, 128, 128), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("16x16").And.Contain("17x16"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => TheoraVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("Theo"),
      Width = 16,
      Height = 16,
    }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  [TestCase(-1, 16)]
  [Category("Unit")]
  public void NonPositiveGeometryIsRefused(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => TheoraVideoEncoder.Create(_Stream(width, height)));
    Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("Theo"),
    FrameRate = new Rational(25, 1),
    TimeBase = new Rational(1, 25),
    Width = width,
    Height = height,
  };

  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var samples = checked(width * height);
    var planes = new byte[checked(samples * 3)];
    planes.AsSpan(0, samples).Fill(luminance);
    planes.AsSpan(samples).Fill(chrominance);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = planes };
  }
}
