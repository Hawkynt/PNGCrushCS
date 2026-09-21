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
      Assert.That(frame.ColorInfo?.ChromaLocation, Is.EqualTo(RawChromaLocation.Center));
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

  [TestCase("411", PixelFormat.Yuv411P8, 8, 4, 1)]
  [TestCase("420p9", PixelFormat.Yuv420P9, 9, 2, 2)]
  [TestCase("422p9", PixelFormat.Yuv422P9, 9, 2, 1)]
  [TestCase("444p9", PixelFormat.Yuv444P9, 9, 1, 1)]
  [TestCase("420p14", PixelFormat.Yuv420P14, 14, 2, 2)]
  [TestCase("422p14", PixelFormat.Yuv422P14, 14, 2, 1)]
  [TestCase("444p14", PixelFormat.Yuv444P14, 14, 1, 1)]
  [Category("Unit")]
  public void ExtendedYuv4MpegLayoutsUseNativeRawImageRepresentations(
    string chroma,
    PixelFormat expectedFormat,
    int bitDepth,
    int subsampleX,
    int subsampleY
  ) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      CodecId = "rawvideo",
      Width = 4,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes(chroma),
    };
    var expected = new RawImage { Width = 4, Height = 2, Format = expectedFormat, PixelData = [] };
    var payload = Enumerable.Range(0, checked((int)expected.MinimumPixelDataLength)).Select(static i => (byte)i).ToArray();

    var decoder = RawPlanarVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new CodedPacket(0, payload), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(expectedFormat));
      Assert.That(RawImage.YuvBitDepth(frame.Format), Is.EqualTo(bitDepth));
      Assert.That(RawImage.YuvSubsampling(frame.Format), Is.EqualTo((subsampleX, subsampleY)));
      Assert.That(frame.PixelData, Is.EqualTo(payload));
    });
  }

  [TestCase("420", RawChromaLocation.Center)]
  [TestCase("420jpeg", RawChromaLocation.Center)]
  [TestCase("420mpeg2", RawChromaLocation.Left)]
  [TestCase("420paldv", RawChromaLocation.TopLeft)]
  [Category("Unit")]
  public void FourTwentySpellingsCarryTheirChromaSampleLocation(string chroma, RawChromaLocation expectedLocation) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      Width = 4,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes(chroma),
    };
    var payload = Enumerable.Range(0, 12).Select(static i => (byte)(i + 1)).ToArray();

    var decoder = RawPlanarVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new CodedPacket(0, payload), out var decoded), Is.True);
    Assert.That(decoded.ColorInfo?.ChromaLocation, Is.EqualTo(expectedLocation));

    var encoder = RawPlanarVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(decoded, 3, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
      Assert.That(Encoding.ASCII.GetString(encoder.DescribeStream().CodecPrivateData.Span), Is.EqualTo(chroma));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncoderRefusesToRelabelKnownChromaSitingOrInventLeftSitedSamples() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 4,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes("420mpeg2"),
    };
    var encoder = RawPlanarVideoEncoder.Create(stream);
    var mismatched = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = new byte[12],
      ColorInfo = new() { ChromaLocation = RawChromaLocation.Center },
    };
    var rgb = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[32],
    };

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(mismatched, null, out _));
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(rgb, null, out _));
    });
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreIndependentAllIntraPicturesWithNoReferenceOrReorderState() {
    var stream = new MediaStreamInfo {
      Index = 5,
      Kind = MediaStreamKind.Video,
      Width = 2,
      Height = 2,
      CodecPrivateData = Encoding.ASCII.GetBytes("444"),
    };
    var firstPixels = Enumerable.Range(0, 12).Select(static i => (byte)i).ToArray();
    var secondPixels = Enumerable.Range(0, 12).Select(static i => (byte)(200 + i)).ToArray();
    var first = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Yuv444P8, PixelData = firstPixels };
    var second = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Yuv444P8, PixelData = secondPixels };
    var encoder = RawPlanarVideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(first, 9, out var firstPacket), Is.True);
    Assert.That(encoder.TryEncode(second, 2, out var secondPacket), Is.True);
    firstPixels[0] = 255;
    secondPixels[0] = 0;

    Assert.Multiple(() => {
      Assert.That(firstPacket.IsKeyFrame, Is.True);
      Assert.That(secondPacket.IsKeyFrame, Is.True);
      Assert.That(firstPacket.PresentationTimestamp, Is.EqualTo(9));
      Assert.That(firstPacket.DecodeTimestamp, Is.EqualTo(9));
      Assert.That(secondPacket.PresentationTimestamp, Is.EqualTo(2));
      Assert.That(secondPacket.DecodeTimestamp, Is.EqualTo(2));
      Assert.That(firstPacket.Data.Span[0], Is.EqualTo(0), "the packet must not retain the source frame buffer");
      Assert.That(secondPacket.Data.Span[0], Is.EqualTo(200), "later frames must not share packet state");
    });
  }

  [Test]
  [Category("Unit")]
  public void GenericRawvideoNameWithoutAPlanarLayoutIsNotClaimed() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      CodecId = "rawvideo",
      Width = 4,
      Height = 2,
    };

    Assert.That(RawPlanarVideoDecoder.Accepts(stream), Is.False);
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

  [Test]
  [Category("Conformance")]
  public void FfmpegReadsMpeg2SitedYuv4MpegOutput() {
    FFmpegOracle.RequireAvailable();

    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 4,
      Height = 2,
      FrameRate = new Rational(25, 1),
      TimeBase = new Rational(1, 25),
      CodecPrivateData = Encoding.ASCII.GetBytes("420mpeg2"),
    };
    var frame = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = [16, 32, 48, 64, 80, 96, 112, 128, 90, 100, 140, 150],
      ColorInfo = new() { ChromaLocation = RawChromaLocation.Left },
    };
    var encoder = RawPlanarVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var data = VideoIO.Mux<Yuv4MpegWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".y4m");

    try {
      File.WriteAllBytes(path, data);
      var (decoded, output) = FFmpegOracle.TryDecodeFirstFrame(path, 4, 2);
      Assert.That(decoded, Is.True, output);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }
}
