using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.ZeroCodec.Tests;

[TestFixture]
public class ZeroCodecVideoEncoderTests {

  private static readonly CodecTag _Zeco = CodecTag.FromCharacters("ZECO");

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    MediaStreamKind kind = MediaStreamKind.Video,
    int index = 0) => new() {
    Index = index,
    Kind = kind,
    Codec = _Zeco,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Frame(int width, int height, params byte[] planes) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Yuv422P8,
    PixelData = planes,
  };

  private static byte[] _Inflate(ReadOnlyMemory<byte> data, int count) {
    using var source = new MemoryStream(data.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    var result = new byte[count];
    zlib.ReadExactly(result);
    Assert.That(zlib.ReadByte(), Is.EqualTo(-1));
    return result;
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheZeroCodecEncoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(8, 3));

    Assert.That(encoder, Is.TypeOf<ZeroCodecVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var described = ZeroCodecVideoEncoder.Create(_Stream(8, 3, index: 2)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(ZeroCodecVideoEncoder.Codec, Is.EqualTo(_Zeco));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Codec, Is.EqualTo(_Zeco));
      Assert.That(described.Handler, Is.EqualTo(_Zeco));
      Assert.That(described.Width, Is.EqualTo(8));
      Assert.That(described.Height, Is.EqualTo(3));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(ZeroCodecVideoDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FirstPictureIsALiteralBottomUpKeyFrame() {
    var frame = _Frame(
      2,
      2,
      // Y plane, top row then bottom row.
      16, 17, 235, 234,
      // Cb plane.
      128, 129,
      // Cr plane.
      130, 131);
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 2));

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(_Inflate(packet.Data, 8), Is.EqualTo(new byte[] {
        // Bottom row first.
        129, 235, 131, 234,
        128, 16, 130, 17,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnchangedPictureBecomesAnAllZeroPFrame() {
    var frame = _Frame(2, 1, 16, 17, 128, 130);
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(_Inflate(second.Data, 4), Is.EqualTo(new byte[4]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChangedNonzeroBytesAreLiteralInAPFrame() {
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));
    var first = _Frame(2, 1, 16, 16, 128, 128);
    var second = _Frame(2, 1, 235, 16, 128, 128);

    Assert.That(encoder.TryEncode(first, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.False);
      Assert.That(_Inflate(packet.Data, 4), Is.EqualTo(new byte[] { 0, 235, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AChangeToZeroForcesAKeyFrameBecauseZeroIsThePredictorSentinel() {
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));
    var first = _Frame(2, 1, 16, 16, 128, 128);
    var second = _Frame(2, 1, 0, 16, 128, 128);

    Assert.That(encoder.TryEncode(first, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(_Inflate(packet.Data, 4), Is.EqualTo(new byte[] { 128, 0, 128, 16 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void IAndPFramesRoundTripTheirYuvSamplesExactly() {
    var pictures = new[] {
      _Frame(4, 2, 16, 17, 18, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
      _Frame(4, 2, 16, 17, 80, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
      _Frame(4, 2, 16, 17, 0, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
    };
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(4, 2));
    var decoder = ZeroCodecVideoDecoder.Create(encoder.DescribeStream());

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"picture {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AviPreservesTheKeyFlagNeededToDistinguishIAndPFrames() {
    var stream = _Stream(4, 2);
    var encoder = ZeroCodecVideoEncoder.Create(stream);
    var frame = _Frame(4, 2, 16, 17, 18, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135);
    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [first, second]);
    var container = AviContainer.FromBytes(avi);
    var demuxedStream = AviContainer.Streams(container).Single();
    var packets = AviContainer.ReadPackets(container).ToArray();

    Assert.Multiple(() => {
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
    });

    var decoder = ZeroCodecVideoDecoder.Create(demuxedStream);
    Assert.That(decoder.TryDecode(packets[0], out _), Is.True);
    Assert.That(decoder.TryDecode(packets[1], out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RefusesUnsupportedStreamsAndSourceFrames() {
    Assert.Throws<NotSupportedException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(4, 1, MediaStreamKind.Audio)));
    Assert.Throws<InvalidDataException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(0, 1)));
    Assert.Throws<NotSupportedException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(3, 1)));

    var encoder = ZeroCodecVideoEncoder.Create(_Stream(4, 1));
    Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Frame(4, 2, new byte[16]), 0, out _));
    Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Frame(4, 1, new byte[7]), 0, out _));
    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }
}
