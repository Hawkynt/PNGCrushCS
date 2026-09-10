using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class MatroxUncompressedVideoCodecTests {

  private static readonly CodecTag _M101 = CodecTag.FromCharacters("M101");
  private static readonly CodecTag _M102 = CodecTag.FromCharacters("M102");

  [Test]
  [Category("Unit")]
  public void BothMatroxTagsAreRegisteredForReadAndWrite() {
    var sd = _Stream(_M101, 2, 1, 16);
    var hd = _Stream(_M102, 2, 1, 16);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.AllCodecs.Select(static codec => codec.CodecName), Does.Contain("Matrox Uncompressed SD"));
      Assert.That(VideoFormatRegistry.AllCodecs.Select(static codec => codec.CodecName), Does.Contain("Matrox Uncompressed HD"));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(static codec => codec.CodecName), Does.Contain("Matrox Uncompressed SD"));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(static codec => codec.CodecName), Does.Contain("Matrox Uncompressed HD"));
      Assert.That(VideoFormatRegistry.CanEncode(sd), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(hd), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void M102UsesTheSameEightBitLayoutAsM101() {
    var decoder = M102VideoDecoder.Create(_DecoderStream(_M102, 2, 1, 8, 4, 3));

    Assert.That(decoder.TryDecode(new(0, new byte[] { 16, 128, 235, 128 }), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
  }

  [TestCase("M101")]
  [TestCase("M102")]
  [Category("Unit")]
  public void EightBitPlanesPackAsYuyv(string tag) {
    var codec = CodecTag.FromCharacters(tag);
    var encoder = _Encoder(codec, _Stream(codec, 4, 1, 16));
    var frame = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Yuv422P8,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80],
    };

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 10, 50, 20, 70, 30, 60, 40, 80 }));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
    });
  }

  [TestCase("M101")]
  [TestCase("M102")]
  [Category("Unit")]
  public void TenBitPlanesUseTheMatroxLowBitSideband(string tag) {
    var codec = CodecTag.FromCharacters(tag);
    var encoder = _Encoder(codec, _Stream(codec, 2, 1, 20));
    var frame = _Yuv422P10(2, 1, [1, 1022], [341], [682]);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var bytes = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(40));
      Assert.That(bytes[..4], Is.EqualTo(new byte[] { 0, 85, 255, 170 }));
      Assert.That(bytes[32], Is.EqualTo(0xA5));
      Assert.That(bytes.AsSpan(4, 28).ToArray(), Is.All.Zero);
      Assert.That(bytes.AsSpan(33, 7).ToArray(), Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamWritesTheMatroxTrailerAndTheRequestedTag() {
    var encoder = M102VideoEncoder.Create(_Stream(_M102, 18, 3, 20));
    var described = encoder.DescribeStream();
    var format = described.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(_M102));
      Assert.That(described.Handler, Is.EqualTo(_M102));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(format, Has.Length.EqualTo(64));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(16)), Is.EqualTo(_M102.Value));
      Assert.That(format[48], Is.EqualTo(10));
      Assert.That(format[52], Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(60)), Is.EqualTo(80));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(20)), Is.EqualTo(240));
      Assert.That(M102VideoDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void ExistingTrailerKeepsItsUnknownBytesAndFieldOrder() {
    var privateData = _PrivateData(_M101, 2, 4, 8, 4, 0);
    privateData[40 + 5] = 0xA7;
    var encoder = M101VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _M101,
      Width = 2,
      Height = 4,
      BitsPerPixel = 16,
      CodecPrivateData = privateData,
    });
    var frame = new RawImage {
      Width = 2,
      Height = 4,
      Format = PixelFormat.Yuv422P8,
      PixelData = [
        10, 10,
        20, 20,
        30, 30,
        40, 40,
        128, 128, 128, 128,
        128, 128, 128, 128,
      ],
    };

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var bytes = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(20));
      Assert.That(bytes[4], Is.EqualTo(40));
      Assert.That(bytes[8], Is.EqualTo(10));
      Assert.That(bytes[12], Is.EqualTo(30));
      Assert.That(encoder.DescribeStream().CodecPrivateData.Span[40 + 5], Is.EqualTo(0xA7));
      Assert.That(encoder.DescribeStream().CodecPrivateData.Span[52], Is.Zero);
    });
  }

  [TestCase("M101", 16)]
  [TestCase("M102", 20)]
  [Category("Unit")]
  public void PacketsMuxIntoAviAndComeBackToTheMatchingDecoder(string tag, int bitsPerPixel) {
    var codec = CodecTag.FromCharacters(tag);
    var encoder = _Encoder(codec, _Stream(codec, 2, 1, bitsPerPixel));
    var frame = bitsPerPixel == 16
      ? new RawImage { Width = 2, Height = 1, Format = PixelFormat.Yuv422P8, PixelData = [16, 235, 128, 128] }
      : _Yuv422P10(2, 1, [64, 940], [512], [512]);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decodedPacket = AviContainer.ReadPackets(container).Single();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(codec));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(64));
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    });

    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder.TryDecode(decodedPacket, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TenBitEncoderRefusesSamplesOutsideTenBits() {
    var encoder = M101VideoEncoder.Create(_Stream(_M101, 2, 1, 20));
    var frame = _Yuv422P10(2, 1, [64, 1024], [512], [512]);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
    Assert.That(failure!.Message, Does.Contain("1024"));
  }

  [Test]
  [Category("Unit")]
  public void EncoderRefusesUnsupportedGeometryDepthAndFrameChanges() {
    Assert.Throws<NotSupportedException>(() => M101VideoEncoder.Create(_Stream(_M101, 3, 1, 16)));
    Assert.Throws<NotSupportedException>(() => M101VideoEncoder.Create(_Stream(_M101, 2, 1, 12)));

    var encoder = M101VideoEncoder.Create(_Stream(_M101, 2, 2, 16));
    var wrong = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Yuv422P8, PixelData = [16, 16, 128, 128] };
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));

    var shortFrame = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Yuv422P8, PixelData = [16] };
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(shortFrame, 0, out _));
  }

  private static MediaStreamInfo _Stream(CodecTag codec, int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static MediaStreamInfo _DecoderStream(
    CodecTag codec,
    int width,
    int height,
    byte bits,
    int stride,
    byte fieldFlags
  ) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec,
    Width = width,
    Height = height,
    BitsPerPixel = 16,
    CodecPrivateData = _PrivateData(codec, width, height, bits, stride, fieldFlags),
  };

  private static byte[] _PrivateData(
    CodecTag codec,
    int width,
    int height,
    byte bits,
    int stride,
    byte fieldFlags
  ) {
    var format = new byte[64];
    BinaryPrimitives.WriteUInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(16), codec.Value);
    format[48] = bits;
    format[52] = fieldFlags;
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(60), checked((uint)stride));
    return format;
  }

  private static IVideoPacketEncoder _Encoder(CodecTag codec, MediaStreamInfo stream)
    => codec == _M101 ? M101VideoEncoder.Create(stream) : M102VideoEncoder.Create(stream);

  private static RawImage _Yuv422P10(
    int width,
    int height,
    ReadOnlySpan<ushort> luma,
    ReadOnlySpan<ushort> cb,
    ReadOnlySpan<ushort> cr
  ) {
    var data = new byte[(luma.Length + cb.Length + cr.Length) * 2];
    var at = 0;
    foreach (var plane in new[] { luma.ToArray(), cb.ToArray(), cr.ToArray() })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), sample);
        at += 2;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P10,
      PixelData = data,
    };
  }
}
