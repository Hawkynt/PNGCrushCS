using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Lcl.Tests;

/// <summary>
/// The LCL ZLIB encoder measured against this package's own decoder: the stream it describes is one
/// the decoder accepts, every packet it writes decodes to exactly the picture it was given, and what
/// it will not take it refuses by name.
/// </summary>
[TestFixture]
public class LclZlibVideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = CodecTag.FromCharacters("avc1"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    DeclaredFrameCount = 6,
  };

  private static byte[] _Inflate(ReadOnlyMemory<byte> packet) {
    using var source = new MemoryStream(packet.ToArray());
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAcceptsAndCreates() {
    var encoder = LclZlibVideoEncoder.Create(_Stream(13, 7));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("ZLIB")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("ZLIB")));
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
      Assert.That(format[16..20], Is.EqualTo("ZLIB"u8.ToArray()));
      Assert.That(format[40], Is.EqualTo(4), "the format's own always-[4,0,0,0] field");
      Assert.That(format[44], Is.EqualTo(2), "image type RGB24");
      Assert.That(format[46], Is.Zero, "no flags");
      Assert.That(format[47], Is.EqualTo(3), "codec ZLIB");
    });

    Assert.That(LclZlibVideoDecoder.Accepts(stream), Is.True);
    Assert.DoesNotThrow(() => LclZlibVideoDecoder.Create(stream));
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<LclZlibVideoDecoder>());
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

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
    var encoder = LclZlibVideoEncoder.Create(_Stream(width, height));
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
  public void PacketsInflateToThePackedPictureBottomRowFirst() {
    var picture = LosslessEncoderPictures.Noise(13, 7, PixelFormat.Bgr24, seed: 3);
    var encoder = LclZlibVideoEncoder.Create(_Stream(13, 7));

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var inflated = _Inflate(packet.Data);

    Assert.That(inflated, Has.Length.EqualTo(13 * 3 * 7), "rows packed tight, no four-byte padding");
    Assert.That(inflated.AsSpan(0, 39).ToArray(), Is.EqualTo(picture.PixelData.AsSpan(6 * 39, 39).ToArray()), "first coded row is the bottom one");
    Assert.That(inflated.AsSpan(6 * 39, 39).ToArray(), Is.EqualTo(picture.PixelData.AsSpan(0, 39).ToArray()), "last coded row is the top one");
  }

  [Test]
  [Category("Unit")]
  public void PassesTimestampsThrough() {
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 5);
    var encoder = LclZlibVideoEncoder.Create(_Stream(4, 4));

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
    var encoder = LclZlibVideoEncoder.Create(_Stream(20, 6));
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

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.Throws<NotSupportedException>(() => LclZlibVideoEncoder.Create(_Stream(4, 4, MediaStreamKind.Audio)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<NotSupportedException>(() => LclZlibVideoEncoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void RefusesAPictureThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = LclZlibVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMidStreamGeometryChange() {
    var encoder = LclZlibVideoEncoder.Create(_Stream(8, 8));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 1);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfPixelData() {
    var encoder = LclZlibVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Bgr24, PixelData = new byte[10] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
  }
}
