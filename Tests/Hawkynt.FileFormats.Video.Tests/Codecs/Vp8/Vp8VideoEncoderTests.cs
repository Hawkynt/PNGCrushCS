using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Codecs.Vp8;

[TestFixture]
public sealed class Vp8VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void EncodeProducesStandaloneRfc6386KeyFrameAndDescribesMuxableStream() {
    const int width = 17;
    const int height = 13;
    var requested = new MediaStreamInfo {
      Index = 2,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 2,
    };
    var image = _Flat(width, height, 96);

    var encoder = Vp8VideoEncoder.Create(requested);
    var stream = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(Vp8VideoEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("VP80")));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("VP80")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("VP80")));
      Assert.That(stream.CodecId, Is.EqualTo("V_VP8"));
      Assert.That(stream.Width, Is.EqualTo(width));
      Assert.That(stream.Height, Is.EqualTo(height));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
    });

    Assert.That(encoder.TryEncode(image, 12, out var packet), Is.True);
    var data = packet.Data.ToArray();
    Assert.That(data.Length, Is.GreaterThanOrEqualTo(10));

    var frameTag = data[0] | data[1] << 8 | data[2] << 16;
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(2));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(12));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(12));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(frameTag & 1, Is.Zero, "frame_type must be key frame");
      Assert.That(frameTag >> 1 & 7, Is.Zero, "the encoder writes VP8 bitstream version 0");
      Assert.That(frameTag >> 4 & 1, Is.EqualTo(1), "show_frame must be set");
      Assert.That(frameTag >> 5, Is.GreaterThan(0), "the first partition must not be empty");
      Assert.That(data[3..6], Is.EqualTo(new byte[] { 0x9D, 0x01, 0x2A }));
      Assert.That((data[6] | data[7] << 8) & 0x3FFF, Is.EqualTo(width));
      Assert.That((data[8] | data[9] << 8) & 0x3FFF, Is.EqualTo(height));
    });

    var freshDecoder = Vp8VideoDecoder.Create(stream);
    Assert.That(freshDecoder.TryDecode(packet, out var decoded), Is.True,
      "an all-keyframe encoder must make every packet independently decodable");
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.All.InRange((byte)90, (byte)102));
    });
  }

  [Test]
  [Category("Unit")]
  public void ConsecutivePacketsRemainIndependentKeyFrames() {
    var stream = _Stream(16, 16);
    var encoder = Vp8VideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(_Flat(16, 16, 48), 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(_Flat(16, 16, 192), 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.True);
    });

    var decoder = Vp8VideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True,
      "the second packet must decode without the first packet's reference state");
    Assert.That(decoded.PixelData.Average(static sample => sample), Is.GreaterThan(170));
  }

  [Test]
  [Category("Unit")]
  public void CreateRejectsNonVideoAndDimensionsTheVp8HeaderCannotRepresent() {
    var audio = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Width = 16,
      Height = 16,
    };

    Assert.Throws<NotSupportedException>(() => Vp8VideoEncoder.Create(audio));
    Assert.Throws<NotSupportedException>(() => Vp8VideoEncoder.Create(_Stream(0, 16)));
    Assert.Throws<NotSupportedException>(() => Vp8VideoEncoder.Create(_Stream(16384, 16)));
    Assert.Throws<NotSupportedException>(() => Vp8VideoEncoder.Create(_Stream(16, 16384)));
  }

  [Test]
  [Category("Unit")]
  public void EncodeRejectsMidStreamGeometryChangeAndTruncatedPixelData() {
    var encoder = Vp8VideoEncoder.Create(_Stream(16, 16));
    var wrongSize = _Flat(8, 8, 0);
    var truncated = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[16 * 16 * 3 - 1],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongSize, 0, out _));
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(truncated, 0, out _));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Flat(int width, int height, byte value) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgb24,
    PixelData = Enumerable.Repeat(value, width * height * 3).ToArray(),
  };
}
