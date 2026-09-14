using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Matroska;

namespace FileFormat.Codecs.HuffYuv.Tests;

[TestFixture]
public class HuffYuvEncoderTests {

  private const int _BITMAP_INFO_HEADER_SIZE = 40;
  private const byte _PROGRESSIVE = 0x20;
  private const byte _INTERLACED = 0x10;
  private const byte _TABLES_PER_FRAME = 0x40;
  private const byte _CHROMA = 0x01;

  [Test]
  [Category("Unit")]
  public void InterlacedFourTwoZeroWithPacketTablesIsDescribedExactly() {
    var encoder = HuffYuvEncoder.Create(
      _Request(16, 8, 12, timeBase: new Rational(1, 25)),
      HuffYuvPredictionMethod.Median,
      interlaced: true,
      tablesPerFrame: true);

    var described = encoder.DescribeStream();
    var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(described.BitsPerPixel, Is.EqualTo(12));
      Assert.That(extra[0], Is.EqualTo(2), "median predictor");
      Assert.That(extra[1], Is.EqualTo(12), "4:2:0 second-form bitstream depth");
      Assert.That(extra[2], Is.EqualTo(_INTERLACED | _TABLES_PER_FRAME));
      Assert.That(extra[3], Is.Zero);
      Assert.That(HuffYuvDecoder.Create(described), Is.Not.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void SecondFormYuvRoundTripsAllPredictorsProgressiveAndInterlaced() {
    foreach (var format in new[] { PixelFormat.Yuv420P8, PixelFormat.Yuv422P8 })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>())
        foreach (var interlaced in new[] { false, true }) {
          var bpp = format == PixelFormat.Yuv420P8 ? 12 : 16;
          var frame = _Random(format, 16, 8, 1000 + bpp * 10 + (int)prediction * 2 + (interlaced ? 1 : 0));
          var encoder = HuffYuvEncoder.Create(_Request(16, 8, bpp), prediction, interlaced: interlaced);

          Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
          Assert.Multiple(() => {
            Assert.That(packet.IsKeyFrame, Is.True);
            Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
            Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
            Assert.That(_Decode(encoder.DescribeStream(), packet), Is.EqualTo(_Expected(frame)), $"{format}, {prediction}, interlaced={interlaced}");
          });
        }
  }

  [Test]
  [Category("Unit")]
  public void PackedColourRoundTripsBothSupportedPredictorsProgressiveAndInterlaced() {
    foreach (var (format, bpp) in new[] { (PixelFormat.Rgb24, 24), (PixelFormat.Bgra32, 32) })
      foreach (var prediction in new[] { HuffYuvPredictionMethod.Left, HuffYuvPredictionMethod.Gradient })
        foreach (var interlaced in new[] { false, true }) {
          var frame = _Random(format, 11, 7, bpp * 100 + (int)prediction * 10 + (interlaced ? 1 : 0));
          var encoder = HuffYuvEncoder.Create(_Request(11, 7, bpp), prediction, interlaced: interlaced);

          Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
          Assert.That(_Decode(encoder.DescribeStream(), packet), Is.EqualTo(_Expected(frame)), $"{format}, {prediction}, interlaced={interlaced}");
        }
  }

  [Test]
  [Category("Unit")]
  public void ThirdFormYuvRoundTripsEveryRepresentableSubsampling() {
    foreach (var (format, horizontalShift, verticalShift) in new[] {
      (PixelFormat.Yuv420P8, 1, 1),
      (PixelFormat.Yuv422P8, 1, 0),
      (PixelFormat.Yuv440P8, 0, 1),
      (PixelFormat.Yuv444P8, 0, 0),
    })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>())
        foreach (var interlaced in new[] { false, true }) {
          var frame = _Random(format, 16, 8, 2000 + (int)format * 31 + (int)prediction * 2 + (interlaced ? 1 : 0));
          var encoder = _PlanarYuvEncoder(16, 8, prediction, horizontalShift, verticalShift, interlaced, tablesPerFrame: false);

          Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
          var described = encoder.DescribeStream();
          var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];
          Assert.Multiple(() => {
            Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("FFVH")));
            Assert.That(extra[1], Is.EqualTo(0x70 | horizontalShift | (verticalShift << 2)));
            Assert.That(extra[2] & _CHROMA, Is.EqualTo(_CHROMA));
            Assert.That(extra[2] & (_INTERLACED | _PROGRESSIVE), Is.EqualTo(interlaced ? _INTERLACED : _PROGRESSIVE));
            Assert.That(extra[3], Is.EqualTo(1));
            Assert.That(_Decode(described, packet), Is.EqualTo(_Expected(frame)), $"{format}, {prediction}, interlaced={interlaced}");
          });
        }
  }

  [Test]
  [Category("Unit")]
  public void PlanarGreyAndRgbStillRoundTripAllPredictors() {
    foreach (var (format, bpp, planar) in new[] {
      (PixelFormat.Gray8, 8, true),
      (PixelFormat.Rgb24, 24, true),
      (PixelFormat.Rgba32, 32, true),
    })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>())
        foreach (var interlaced in new[] { false, true }) {
          var frame = _Random(format, 9, 7, 3000 + bpp + (int)prediction * 2 + (interlaced ? 1 : 0));
          var encoder = HuffYuvEncoder.Create(_Request(9, 7, bpp), prediction, planar, interlaced);
          Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
          Assert.That(_Decode(encoder.DescribeStream(), packet), Is.EqualTo(_Expected(frame)));
        }
  }

  [Test]
  [Category("Unit")]
  public void TablesPerFrameMayChangeFromPacketToPacket() {
    var encoder = HuffYuvEncoder.Create(
      _Request(48, 16, 8),
      HuffYuvPredictionMethod.Gradient,
      planar: true,
      tablesPerFrame: true);
    var frames = new[] {
      _Flat(PixelFormat.Gray8, 48, 16, 17),
      _Random(PixelFormat.Gray8, 48, 16, 8128),
      _Flat(PixelFormat.Gray8, 48, 16, 231),
    };
    var packets = new List<CodedPacket>();
    foreach (var frame in frames) {
      Assert.That(encoder.TryEncode(frame, packets.Count, out var packet), Is.True);
      packets.Add(packet);
    }

    var description = encoder.DescribeStream();
    var extra = description.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];
    Assert.That(extra[2] & _TABLES_PER_FRAME, Is.EqualTo(_TABLES_PER_FRAME));

    var decoder = HuffYuvDecoder.Create(description);
    for (var i = 0; i < frames.Length; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(frames[i].PixelData), $"packet {i}");
    }

    Assert.That(packets.Select(static packet => Convert.ToHexString(packet.Data.Span[..Math.Min(16, packet.Data.Length)])), Is.Unique);
  }

  [Test]
  [Category("Unit")]
  public void DescriptionMayBeOnlyCodecBytesRatherThanABitmapHeader() {
    var full = HuffYuvTestStream.Description(
      (byte)HuffYuvPredictionMethod.Gradient,
      0x71,
      (byte)(_PROGRESSIVE | _CHROMA),
      1,
      3);
    var encoder = HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFVH"),
      Width = 16,
      Height = 8,
      BitsPerPixel = 16,
      CodecPrivateData = full[_BITMAP_INFO_HEADER_SIZE..],
    });
    var frame = _Random(PixelFormat.Yuv422P8, 16, 8, 77);

    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
    Assert.That(_Decode(encoder.DescribeStream(), packet), Is.EqualTo(_Expected(frame)));
  }

  [Test]
  [Category("Unit")]
  public void MuxesIntoAviAndMatroskaAndComesBackThroughTheRegistry() {
    var encoder = HuffYuvEncoder.Create(
      _Request(16, 8, 12, timeBase: new Rational(1, 25), frameRate: new Rational(25, 1)),
      HuffYuvPredictionMethod.Median);
    var frames = new[] { _Random(PixelFormat.Yuv420P8, 16, 8, 21), _Random(PixelFormat.Yuv420P8, 16, 8, 22) };
    var packets = new List<CodedPacket>();
    for (var i = 0; i < frames.Length; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i, out var packet), Is.True);
      packets.Add(packet with { Duration = 1 });
    }

    var described = encoder.DescribeStream();
    var avi = AviContainer.FromBytes(VideoIO.Mux<AviWriter>([described], packets));
    var aviFrames = VideoIO.Decode(AviContainer.ReadPackets(avi), AviContainer.Streams(avi)[0], VideoFormatRegistry.CreateDecoder).ToList();
    var mkv = MatroskaContainer.FromBytes(VideoIO.Mux<MatroskaWriter>([described], packets));
    var mkvFrames = VideoIO.Decode(MatroskaContainer.ReadPackets(mkv), MatroskaContainer.Streams(mkv)[0], VideoFormatRegistry.CreateDecoder).ToList();

    Assert.Multiple(() => {
      Assert.That(aviFrames, Has.Count.EqualTo(2));
      Assert.That(mkvFrames, Has.Count.EqualTo(2));
      for (var i = 0; i < frames.Length; ++i) {
        Assert.That(aviFrames[i].Image.PixelData, Is.EqualTo(_Expected(frames[i])), $"AVI frame {i}");
        Assert.That(mkvFrames[i].Image.PixelData, Is.EqualTo(_Expected(frames[i])), $"Matroska frame {i}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesOnlyLayoutsTheEightBitRawModelCannotWrite() {
    Assert.Multiple(() => {
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 20)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("20"));
      Assert.That(() => HuffYuvEncoder.Create(_Request(5, 4, 16)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("even width"));
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 5, 12)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("even height"));
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 24), HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>().With.Message.Contains("median"));
      Assert.That(() => HuffYuvEncoder.Create(_Request(2, 8, 12), HuffYuvPredictionMethod.Median, interlaced: true), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => HuffYuvEncoder.Create(new MediaStreamInfo {
        Index = 0, Kind = MediaStreamKind.Video, Width = 4, Height = 4, BitsPerPixel = 24,
        CodecPrivateData = new byte[] { 0, 0x90, 0x21, 1 },
      }), Throws.TypeOf<NotSupportedException>().With.Message.Contains("10-bit"));
    });
  }

  private static HuffYuvEncoder _PlanarYuvEncoder(
    int width,
    int height,
    HuffYuvPredictionMethod prediction,
    int horizontalShift,
    int verticalShift,
    bool interlaced,
    bool tablesPerFrame) {
    var flags = (byte)((interlaced ? _INTERLACED : _PROGRESSIVE) | _CHROMA | (tablesPerFrame ? _TABLES_PER_FRAME : 0));
    var full = HuffYuvTestStream.Description(
      (byte)prediction,
      (byte)(0x70 | horizontalShift | (verticalShift << 2)),
      flags,
      1,
      3);
    return HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFVH"),
      Width = width,
      Height = height,
      BitsPerPixel = horizontalShift == 1 && verticalShift == 1 ? 12 : horizontalShift + verticalShift == 1 ? 16 : 24,
      CodecPrivateData = full[_BITMAP_INFO_HEADER_SIZE..],
    });
  }

  private static MediaStreamInfo _Request(int width, int height, int bitsPerPixel, Rational? timeBase = null, Rational? frameRate = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = timeBase ?? Rational.Unknown,
    FrameRate = frameRate ?? Rational.Unknown,
  };

  private static byte[] _Decode(MediaStreamInfo described, CodedPacket packet) {
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    return decoded.PixelData;
  }

  private static RawImage _Random(PixelFormat format, int width, int height, int seed) {
    var prototype = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[checked((int)prototype.MinimumPixelDataLength)];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _Flat(PixelFormat format, int width, int height, byte value) {
    var image = _Random(format, width, height, 0);
    Array.Fill(image.PixelData, value);
    return image;
  }

  private static byte[] _Expected(RawImage frame) {
    if (frame.Format == PixelFormat.Bgra32)
      return frame.ToRgba32();
    if (!frame.IsPlanarYuv)
      return frame.PixelData;

    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(frame.Format);
    var (chromaWidth, _) = frame.GetPlaneDimensions(1);
    var luma = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var rgb = new byte[frame.Width * frame.Height * 3];
    for (var y = 0; y < frame.Height; ++y)
      for (var x = 0; x < frame.Width; ++x) {
        var chroma = (y / subsampleY) * chromaWidth + x / subsampleX;
        var scaled = 298 * (luma[y * frame.Width + x] - 16);
        var blue = cb[chroma] - 128;
        var red = cr[chroma] - 128;
        var at = (y * frame.Width + x) * 3;
        rgb[at] = _Clamp(scaled + 409 * red + 128);
        rgb[at + 1] = _Clamp(scaled - 100 * blue - 208 * red + 128);
        rgb[at + 2] = _Clamp(scaled + 516 * blue + 128);
      }
    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
