using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// MSZH encoding against the independently implemented decoder: stream description, exact command
/// bytes, raw fallback, lossless picture round trips, AVI carriage and refusal behaviour.
/// </summary>
[TestFixture]
public sealed class MszhVideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = CodecTag.FromCharacters("MSZH"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    DeclaredFrameCount = 6,
  };

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAndRegistryAccept() {
    var encoder = MszhVideoEncoder.Create(_Stream(13, 7));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MSZH")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("MSZH")));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(13));
      Assert.That(stream.Height, Is.EqualTo(7));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(stream.DeclaredFrameCount, Is.EqualTo(6));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(48));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(13));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(7));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(12)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24));
      Assert.That(format[16..20], Is.EqualTo("MSZH"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(20)), Is.EqualTo(40 * 7));
      Assert.That(format[40], Is.EqualTo(4), "the format's own always-[4,0,0,0] field");
      Assert.That(format[44], Is.EqualTo(2), "image type RGB24");
      Assert.That(format[45], Is.Zero, "MSZH compression");
      Assert.That(format[46], Is.Zero, "single section, no null-frame or PNG-filter flags");
      Assert.That(format[47], Is.EqualTo(1), "codec MSZH");
    });

    Assert.That(MszhVideoDecoder.Accepts(stream), Is.True);
    Assert.DoesNotThrow(() => MszhVideoDecoder.Create(stream));
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<MszhVideoEncoder>());
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MszhVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void ARepeatedFourByteGroupUsesTheReferenceDescriptorLayout() {
    var encoder = MszhVideoEncoder.Create(_Stream(4, 1));
    var picture = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Bgr24,
      PixelData = [1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4],
    };

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    // Mask 0x40: literal, then back-reference. Descriptor 0x0804 is distance four and two
    // four-byte groups: ((2 - 1) << 11) | 4, little-endian on the wire.
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x40, 1, 2, 3, 4, 0x04, 0x08 }));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatWouldGrowFallsBackToTheRawPaddedForm() {
    var encoder = MszhVideoEncoder.Create(_Stream(1, 1));
    var picture = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgr24,
      PixelData = [1, 2, 3],
    };

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 0 }));

    var decoder = MszhVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    LosslessEncoderPictures.AssertSame(picture, decoded);
  }

  [Test]
  [Category("Unit")]
  [TestCase(4, 2, PixelFormat.Bgr24)]
  [TestCase(13, 7, PixelFormat.Bgr24)]
  [TestCase(64, 48, PixelFormat.Bgr24)]
  [TestCase(322, 3, PixelFormat.Rgb24)]
  [TestCase(13, 7, PixelFormat.Bgra32)]
  [TestCase(13, 7, PixelFormat.Gray8)]
  [TestCase(13, 7, PixelFormat.Indexed8)]
  public void RoundTripsASequenceExactly(int width, int height, PixelFormat format) {
    var frames = LosslessEncoderPictures.Sequence(width, height, format, 8, seed: width * 131 + height);
    var encoder = MszhVideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < frames.Length; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True, $"frame {i}: every LCL packet stands on its own");
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      LosslessEncoderPictures.AssertSame(frames[i], decoded, $"frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void PassesTimestampsThrough() {
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 5);
    var encoder = MszhVideoEncoder.Create(_Stream(4, 4));

    Assert.That(encoder.TryEncode(picture, 1234, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(1234));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(1234));
    });

    Assert.That(encoder.TryEncode(picture, null, out packet), Is.True);
    Assert.That(packet.PresentationTimestamp, Is.Null);
    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MuxesIntoAviAndDecodesBackThroughTheContainer() {
    var frames = LosslessEncoderPictures.Sequence(20, 6, PixelFormat.Bgr24, 4, seed: 11);
    var encoder = MszhVideoEncoder.Create(_Stream(20, 6));
    var packets = frames.Select((frame, i) => {
      Assert.That(encoder.TryEncode(frame, i, out var packet), Is.True);
      return packet;
    }).ToArray();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoded = VideoIO.Decode(AviContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder).ToArray();

    Assert.That(decoded, Has.Length.EqualTo(4));
    for (var i = 0; i < decoded.Length; ++i)
      LosslessEncoderPictures.AssertSame(frames[i], decoded[i].Image, $"frame {i}");
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream()
    => Assert.Throws<NotSupportedException>(() => MszhVideoEncoder.Create(_Stream(4, 4, MediaStreamKind.Audio)));

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<NotSupportedException>(() => MszhVideoEncoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void RefusesAPictureThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = MszhVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMidStreamGeometryChange() {
    var encoder = MszhVideoEncoder.Create(_Stream(8, 8));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 1);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfPixelData() {
    var encoder = MszhVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Bgr24, PixelData = new byte[10] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
  }
}
