using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Yuv4Mpeg;
using Hawkynt.FileFormats.Video;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

[TestFixture]
public sealed class RawPlanarVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Yuv4Mpeg420PacketDecodesThroughRegistryWithoutChangingSamples() {
    var y = new byte[] { 16, 32, 48, 64, 80, 96, 112, 128 };
    var u = new byte[] { 90, 100 };
    var v = new byte[] { 140, 150 };
    var payload = y.Concat(u).Concat(v).ToArray();
    var file = Encoding.ASCII.GetBytes("YUV4MPEG2 W4 H2 F25:1 Ip C420jpeg\nFRAME\n")
      .Concat(payload)
      .ToArray();
    var container = Yuv4MpegContainer.FromBytes(file);
    var stream = Yuv4MpegContainer.Streams(container).Single();

    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder.TryDecode(Yuv4MpegContainer.ReadPackets(container).Single(), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(4));
      Assert.That(frame.Height, Is.EqualTo(2));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(frame.PixelData, Is.EqualTo(payload));
      Assert.That(frame.GetPlaneData(0).ToArray(), Is.EqualTo(y));
      Assert.That(frame.GetPlaneData(1).ToArray(), Is.EqualTo(u));
      Assert.That(frame.GetPlaneData(2).ToArray(), Is.EqualTo(v));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncoderProducesMuxable422PacketAndRoundTripsThroughY4M() {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 3,
      Height = 2,
      FrameRate = new Rational(25, 1),
      TimeBase = new Rational(1, 25),
      CodecPrivateData = Encoding.ASCII.GetBytes("422"),
    };
    var pixels = Enumerable.Range(0, 14).Select(static i => (byte)(17 + i)).ToArray();
    var frame = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Yuv422P8,
      PixelData = pixels,
    };

    var encoder = RawPlanarVideoEncoder.Create(requested);
    var described = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("YUV ")));
      Assert.That(described.CodecId, Is.EqualTo("rawvideo"));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(Encoding.ASCII.GetString(described.CodecPrivateData.Span), Is.EqualTo("422"));
    });

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(pixels));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var y4m = VideoIO.Mux<Yuv4MpegWriter>([described], [packet]);
    var roundTrip = Yuv4MpegContainer.FromBytes(y4m);
    var decodedPacket = Yuv4MpegContainer.ReadPackets(roundTrip).Single();
    var decoder = RawPlanarVideoDecoder.Create(Yuv4MpegContainer.Streams(roundTrip).Single());
    Assert.That(decoder.TryDecode(decodedPacket, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void TenBit420PacketUsesNativeRawImageLayout() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      CodecId = "rawvideo",
      Width = 2,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes("420p10"),
    };
    var decoder = RawPlanarVideoDecoder.Create(stream);
    var payload = Enumerable.Range(0, 12).Select(static i => (byte)i).ToArray();

    Assert.That(decoder.TryDecode(new CodedPacket(0, payload), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P10));
      Assert.That(frame.MinimumPixelDataLength, Is.EqualTo(12));
      Assert.That(frame.PixelData, Is.EqualTo(payload));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChromaSitingModesNotRepresentableByPixelFormatAreRejected() {
    foreach (var chroma in new[] { "420mpeg2", "420paldv" }) {
      var stream = new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = CodecTag.FromCharacters("YUV "),
        Width = 4,
        Height = 2,
        CodecPrivateData = Encoding.ASCII.GetBytes(chroma),
      };

      Assert.Throws<NotSupportedException>(() => RawPlanarVideoDecoder.Create(stream), chroma);
      Assert.Throws<NotSupportedException>(() => RawPlanarVideoEncoder.Create(stream), chroma);
    }
  }

  [Test]
  [Category("Unit")]
  public void DecoderRejectsWrongPacketSizeAndStream() {
    var stream = new MediaStreamInfo {
      Index = 3,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      Width = 4,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes("420jpeg"),
    };
    var decoder = RawPlanarVideoDecoder.Create(stream);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new CodedPacket(3, new byte[11]), out _));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new CodedPacket(2, new byte[12]), out _));
  }
}
