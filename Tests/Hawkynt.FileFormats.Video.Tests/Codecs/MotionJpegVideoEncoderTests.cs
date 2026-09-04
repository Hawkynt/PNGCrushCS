using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Mjpeg;
using Hawkynt.FileFormats.Video;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

[TestFixture]
public sealed class MotionJpegVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void EncodeProducesCompleteKeyFrameAndDescribesMuxableStream() {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      Width = 8,
      Height = 8,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 1,
    };
    var pixels = Enumerable.Repeat((byte)96, 8 * 8).ToArray();
    var image = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Gray8,
      PixelData = pixels,
    };

    var encoder = MotionJpegVideoEncoder.Create(requested);
    var stream = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MJPG")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("MJPG")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MJPEG"));
      Assert.That(stream.Width, Is.EqualTo(8));
      Assert.That(stream.Height, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
    });

    Assert.That(encoder.TryEncode(image, 12, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(12));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(12));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.Data.Span[0], Is.EqualTo(0xFF));
      Assert.That(packet.Data.Span[1], Is.EqualTo(0xD8));
      Assert.That(packet.Data.Span[^2], Is.EqualTo(0xFF));
      Assert.That(packet.Data.Span[^1], Is.EqualTo(0xD9));
    });

    var rawStream = VideoIO.Mux<MjpegWriter>([stream], [packet]);
    Assert.That(rawStream, Is.EqualTo(packet.Data.ToArray()));

    var container = MjpegContainer.FromBytes(rawStream);
    var decoded = VideoIO.Decode(
        MjpegContainer.ReadPackets(container),
        MjpegContainer.Streams(container)[0],
        VideoFormatRegistry.CreateDecoder)
      .Single()
      .Image;

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(8));
      Assert.That(decoded.Height, Is.EqualTo(8));
      Assert.That(decoded.ToRgb24().Chunk(3).Select(pixel => pixel[0]), Is.All.InRange(94, 98));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeRejectsMidStreamGeometryChange() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 16,
      Height = 16,
    };
    var encoder = MotionJpegVideoEncoder.Create(stream);
    var wrongSize = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[8 * 8 * 3],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongSize, 0, out _));
  }
}
