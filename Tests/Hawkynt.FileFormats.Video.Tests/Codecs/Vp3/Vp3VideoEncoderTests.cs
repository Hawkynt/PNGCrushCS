using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>The VP3.1 writer and the VP3.0 header normalization at its decode boundary.</summary>
[TestFixture]
public sealed class Vp3VideoEncoderTests {
  private static readonly CodecTag _Vp31 = CodecTag.FromCharacters("VP31");

  private static MediaStreamInfo _Stream(int width, int height, string code = "VP31", int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _GreyRamp(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)((x * 17 + y * 11) & 0xFF);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheVp31EncoderAndItsDescriptionRoundTripsToTheDecoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(31, 19, index: 7));
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(encoder, Is.InstanceOf<Vp3VideoEncoder>());
      Assert.That(Vp3VideoEncoder.Codec, Is.EqualTo(_Vp31));
      Assert.That(described.Codec, Is.EqualTo(_Vp31));
      Assert.That(described.Handler, Is.EqualTo(_Vp31));
      Assert.That(described.Index, Is.EqualTo(7));
      Assert.That(described.Width, Is.EqualTo(31));
      Assert.That(described.Height, Is.EqualTo(19));
      Assert.That(Vp3VideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [TestCase(16, 16)]
  [TestCase(31, 19)]
  [TestCase(33, 17)]
  [Category("Unit")]
  public void EncodesARealVp31PacketAndTheDecoderReadsIt(int width, int height) {
    var encoder = Vp3VideoEncoder.Create(_Stream(width, height));
    var source = _GreyRamp(width, height);

    Assert.That(encoder.TryEncode(source, 42, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.GreaterThan(3));
    Assert.That(packet.Data.Span[0], Is.EqualTo(0x3F)); // intra + finest quantiser
    Assert.That(packet.Data.Span[1], Is.EqualTo(0x00)); // width/height codes
    Assert.That(packet.Data.Span[2], Is.EqualTo(0x08)); // VP3.1, normal coding type, reserved zero
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
    Assert.That(packet.IsKeyFrame, Is.True);

    var decoder = Vp3VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(width * height * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Vp30KeyFrameIsTheSamePayloadWithTheVp31VersionByteAbsent() {
    const int width = 32;
    const int height = 16;
    var encoder = Vp3VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(_GreyRamp(width, height), 0, out var vp31), Is.True);

    var vp30Bytes = new byte[vp31.Data.Length - 1];
    vp31.Data.Span[..2].CopyTo(vp30Bytes);
    vp31.Data.Span[3..].CopyTo(vp30Bytes.AsSpan(2));

    var vp31Decoder = Vp3VideoDecoder.Create(_Stream(width, height, "VP31"));
    var vp30Decoder = Vp3VideoDecoder.Create(_Stream(width, height, "VP30"));
    Assert.That(vp31Decoder.TryDecode(vp31, out var one), Is.True);
    Assert.That(vp30Decoder.TryDecode(new(0, vp30Bytes, IsKeyFrame: true), out var zero), Is.True);

    Assert.That(zero.PixelData, Is.EqualTo(one.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RefusesWrongGeometryAndNonVideoStreams() {
    var encoder = Vp3VideoEncoder.Create(_Stream(16, 16));
    Assert.That(
      () => encoder.TryEncode(_GreyRamp(17, 16), 0, out _),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("16x16"));

    Assert.Throws<NotSupportedException>(() => Vp3VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = _Vp31,
      Width = 16,
      Height = 16,
    }));
  }
}
