using System;
using System.Buffers.Binary;
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

  [Test, Category("Unit")]
  public void InterlacedFourTwoZeroWithPacketTablesIsDescribedExactly() {
    var encoder = HuffYuvEncoder.Create(_Request(16, 8, 12, timeBase: new Rational(1, 25)), HuffYuvPredictionMethod.Median, interlaced: true, tablesPerFrame: true);
    var described = encoder.DescribeStream();
    var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];
    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(described.BitsPerPixel, Is.EqualTo(12));
      Assert.That(extra, Has.Length.EqualTo(4));
      Assert.That(extra[0], Is.EqualTo(2));
      Assert.That(extra[1], Is.EqualTo(12));
      Assert.That(extra[2], Is.EqualTo(_INTERLACED | _TABLES_PER_FRAME));
      Assert.That(extra[3], Is.Zero);
      Assert.That(HuffYuvDecoder.Create(described), Is.Not.Null);
    });
  }

  [Test, Category("Unit")]
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
            Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(_Expected8(frame)), $"{format}, {prediction}, interlaced={interlaced}");
          });
        }
  }

  [Test, Category("Unit")]
  public void PackedColourRoundTripsBothSupportedPredictorsProgressiveAndInterlaced() {
    foreach (var (format, bpp) in new[] { (PixelFormat.Rgb24, 24), (PixelFormat.Bgra32, 32) })
      foreach (var prediction in new[] { HuffYuvPredictionMethod.Left, HuffYuvPredictionMethod.Gradient })
        foreach (var interlaced in new[] { false, true }) {
          var frame = _Random(format, 11, 7, bpp * 100 + (int)prediction * 10 + (interlaced ? 1 : 0));
          var encoder = HuffYuvEncoder.Create(_Request(11, 7, bpp), prediction, interlaced: interlaced);
          Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
          Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(_Expected8(frame)), $"{format}, {prediction}, interlaced={interlaced}");
        }
  }

  [Test, Category("Unit")]
  public void ThirdFormYuvRoundTripsEveryEightBitRepresentableSubsampling() {
    foreach (var (format, hShift, vShift) in new[] {
      (PixelFormat.Yuv420P8, 1, 1), (PixelFormat.Yuv422P8, 1, 0), (PixelFormat.Yuv440P8, 0, 1), (PixelFormat.Yuv444P8, 0, 0),
    })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>())
        foreach (var interlaced in new[] { false, true }) {
          var frame = _Random(format, 16, 8, 2000 + (int)format * 31 + (int)prediction * 2 + (interlaced ? 1 : 0));
          var encoder = _PlanarYuvEncoder(16, 8, 8, prediction, hShift, vShift, interlaced, false);
          Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
          var described = encoder.DescribeStream();
          var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];
          Assert.Multiple(() => {
            Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("FFVH")));
            Assert.That(extra[1], Is.EqualTo(0x70 | hShift | (vShift << 2)));
            Assert.That(extra[2] & _CHROMA, Is.EqualTo(_CHROMA));
            Assert.That(extra[2] & (_INTERLACED | _PROGRESSIVE), Is.EqualTo(interlaced ? _INTERLACED : _PROGRESSIVE));
            Assert.That(extra[3], Is.EqualTo(1));
            Assert.That(_Decode(described, packet).PixelData, Is.EqualTo(_Expected8(frame)));
          });
        }
  }

  [Test, Category("Unit")]
  public void HighDepthYuvRoundTripsTenTwelveAndSixteenBitsExactly() {
    foreach (var (format, bits, hShift, vShift) in new[] {
      (PixelFormat.Yuv420P10, 10, 1, 1), (PixelFormat.Yuv422P10, 10, 1, 0), (PixelFormat.Yuv444P10, 10, 0, 0),
      (PixelFormat.Yuv420P12, 12, 1, 1), (PixelFormat.Yuv422P12, 12, 1, 0), (PixelFormat.Yuv444P12, 12, 0, 0),
      (PixelFormat.Yuv420P16, 16, 1, 1), (PixelFormat.Yuv422P16, 16, 1, 0), (PixelFormat.Yuv444P16, 16, 0, 0),
    })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>()) {
        var frame = _RandomHighDepth(format, 16, 8, bits, 4000 + (int)format * 13 + (int)prediction);
        var encoder = _PlanarYuvEncoder(16, 8, bits, prediction, hShift, vShift, interlaced: true, tablesPerFrame: false);
        Assert.That(encoder.TryEncode(frame, 5, out var packet), Is.True);
        var decoded = _Decode(encoder.DescribeStream(), packet);
        Assert.Multiple(() => {
          Assert.That(decoded.Format, Is.EqualTo(format));
          Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData), $"{format}, {prediction}");
          Assert.That(packet.IsKeyFrame, Is.True);
          Assert.That(packet.DecodeTimestamp, Is.EqualTo(packet.PresentationTimestamp));
        });
      }
  }

  [Test, Category("Unit")]
  public void SixteenBitResidualLowBitsAreNotLost() {
    var frame = _Ramp16(PixelFormat.Yuv444P16, 8, 4);
    var encoder = _PlanarYuvEncoder(8, 4, 16, HuffYuvPredictionMethod.Median, 0, 0, false, false);
    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [Test, Category("Unit")]
  public void HighDepthGreyAndRgbRoundTripWhereRawImageHasAnExactRepresentation() {
    foreach (var (format, bits, alpha) in new[] {
      (PixelFormat.Gray10, 10, false), (PixelFormat.Gray16, 16, false),
      (PixelFormat.Rgb30, 10, false), (PixelFormat.Rgb48, 16, false), (PixelFormat.Rgba64, 16, true),
    }) {
      var frame = _ValidHighPacked(format, 9, 7, bits, 5100 + (int)format);
      var encoder = _PlanarColourEncoder(9, 7, bits, alpha, HuffYuvPredictionMethod.Gradient);
      Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
      var decoded = _Decode(encoder.DescribeStream(), packet);
      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(format));
        Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData), format.ToString());
      });
    }
  }

  [Test, Category("Unit")]
  public void HeaderlessClassicYuvAndRgbUseFixedTablesAndRoundTrip() {
    var yuv = _Random(PixelFormat.Yuv422P8, 16, 8, 6001);
    var yuvEncoder = HuffYuvEncoder.Create(_LegacyRequest(16, 8, 16 | 1)); // left predictor
    Assert.That(yuvEncoder.TryEncode(yuv, null, out var yuvPacket), Is.True);
    var yuvDescription = yuvEncoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(yuvDescription.CodecPrivateData.Length, Is.EqualTo(_BITMAP_INFO_HEADER_SIZE));
      Assert.That(yuvDescription.BitsPerPixel, Is.EqualTo(17));
      Assert.That(_Decode(yuvDescription, yuvPacket).PixelData, Is.EqualTo(_Expected8(yuv)));
    });

    var rgb = _Random(PixelFormat.Rgb24, 11, 7, 6002);
    var rgbEncoder = HuffYuvEncoder.Create(_LegacyRequest(11, 7, 24 | 2)); // left + decorrelation
    Assert.That(rgbEncoder.TryEncode(rgb, null, out var rgbPacket), Is.True);
    Assert.That(_Decode(rgbEncoder.DescribeStream(), rgbPacket).PixelData, Is.EqualTo(rgb.PixelData));
  }

  [Test, Category("Unit")]
  public void LegacyHeightFallbackSelectsInterlacedPredictionAbove288Rows() {
    var frame = _Random(PixelFormat.Yuv422P8, 8, 290, 6010);
    var encoder = HuffYuvEncoder.Create(_LegacyRequest(8, 290, 16 | 3)); // gradient/plane predictor
    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
    Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(_Expected8(frame)));
  }

  [Test, Category("Unit")]
  public void PlanarGreyAndRgbEightBitStillRoundTripAllPredictors() {
    foreach (var (format, bpp) in new[] { (PixelFormat.Gray8, 8), (PixelFormat.Rgb24, 24), (PixelFormat.Rgba32, 32) })
      foreach (var prediction in Enum.GetValues<HuffYuvPredictionMethod>()) {
        var frame = _Random(format, 9, 7, 3000 + bpp + (int)prediction);
        var encoder = HuffYuvEncoder.Create(_Request(9, 7, bpp), prediction, planar: true, interlaced: true);
        Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
        Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(frame.PixelData));
      }
  }

  [Test, Category("Unit")]
  public void TablesPerFrameMayChangeFromPacketToPacket() {
    var encoder = HuffYuvEncoder.Create(_Request(48, 16, 8), HuffYuvPredictionMethod.Gradient, planar: true, tablesPerFrame: true);
    var frames = new[] { _Flat(PixelFormat.Gray8, 48, 16, 17), _Random(PixelFormat.Gray8, 48, 16, 8128), _Flat(PixelFormat.Gray8, 48, 16, 231) };
    var packets = new List<CodedPacket>();
    foreach (var frame in frames) { Assert.That(encoder.TryEncode(frame, packets.Count, out var packet), Is.True); packets.Add(packet); }
    var description = encoder.DescribeStream();
    Assert.That(description.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE + 2] & _TABLES_PER_FRAME, Is.EqualTo(_TABLES_PER_FRAME));
    var decoder = HuffYuvDecoder.Create(description);
    for (var i = 0; i < frames.Length; ++i) { Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True); Assert.That(decoded.PixelData, Is.EqualTo(frames[i].PixelData)); }
    Assert.That(packets.Select(static p => Convert.ToHexString(p.Data.Span[..Math.Min(16, p.Data.Length)])), Is.Unique);
  }

  [Test, Category("Unit")]
  public void MuxesIntoAviAndMatroskaAndComesBackThroughTheRegistry() {
    var encoder = HuffYuvEncoder.Create(_Request(16, 8, 12, timeBase: new Rational(1, 25), frameRate: new Rational(25, 1)), HuffYuvPredictionMethod.Median);
    var frames = new[] { _Random(PixelFormat.Yuv420P8, 16, 8, 21), _Random(PixelFormat.Yuv420P8, 16, 8, 22) };
    var packets = new List<CodedPacket>();
    for (var i = 0; i < frames.Length; ++i) { Assert.That(encoder.TryEncode(frames[i], i, out var packet), Is.True); packets.Add(packet with { Duration = 1 }); }
    var described = encoder.DescribeStream();
    var avi = AviContainer.FromBytes(VideoIO.Mux<AviWriter>([described], packets));
    var aviFrames = VideoIO.Decode(AviContainer.ReadPackets(avi), AviContainer.Streams(avi)[0], VideoFormatRegistry.CreateDecoder).ToList();
    var mkv = MatroskaContainer.FromBytes(VideoIO.Mux<MatroskaWriter>([described], packets));
    var mkvFrames = VideoIO.Decode(MatroskaContainer.ReadPackets(mkv), MatroskaContainer.Streams(mkv)[0], VideoFormatRegistry.CreateDecoder).ToList();
    Assert.Multiple(() => {
      Assert.That(aviFrames, Has.Count.EqualTo(2)); Assert.That(mkvFrames, Has.Count.EqualTo(2));
      for (var i = 0; i < frames.Length; ++i) { Assert.That(aviFrames[i].Image.PixelData, Is.EqualTo(_Expected8(frames[i]))); Assert.That(mkvFrames[i].Image.PixelData, Is.EqualTo(_Expected8(frames[i]))); }
    });
  }

  [Test, Category("Unit")]
  public void RefusesLayoutsWithNoExactRawRepresentation() {
    Assert.Multiple(() => {
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 20)), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => HuffYuvEncoder.Create(_Request(5, 4, 16)), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 5, 12)), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 24), HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => HuffYuvEncoder.Create(_PlanarDescriptionRequest(4, 4, 9, 1, 1, false, false)), Throws.TypeOf<NotSupportedException>());
    });
  }

  private static HuffYuvEncoder _PlanarYuvEncoder(int width, int height, int bits, HuffYuvPredictionMethod prediction, int hShift, int vShift, bool interlaced, bool tablesPerFrame)
    => HuffYuvEncoder.Create(_PlanarDescriptionRequest(width, height, bits, hShift, vShift, interlaced, tablesPerFrame) with { CodecPrivateData = new byte[] {
      (byte)prediction, (byte)(((bits - 1) << 4) | hShift | (vShift << 2)), (byte)((interlaced ? _INTERLACED : _PROGRESSIVE) | _CHROMA | (tablesPerFrame ? _TABLES_PER_FRAME : 0)), 1,
    }});

  private static MediaStreamInfo _PlanarDescriptionRequest(int width, int height, int bits, int hShift, int vShift, bool interlaced, bool tablesPerFrame) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FFVH"), Width = width, Height = height,
    BitsPerPixel = bits, CodecPrivateData = new byte[] { 0, (byte)(((bits - 1) << 4) | hShift | (vShift << 2)), (byte)((interlaced ? _INTERLACED : _PROGRESSIVE) | _CHROMA | (tablesPerFrame ? _TABLES_PER_FRAME : 0)), 1 },
  };

  private static HuffYuvEncoder _PlanarColourEncoder(int width, int height, int bits, bool alpha, HuffYuvPredictionMethod prediction) => HuffYuvEncoder.Create(new MediaStreamInfo {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FFVH"), Width = width, Height = height, BitsPerPixel = bits,
    CodecPrivateData = new byte[] { (byte)prediction, (byte)((bits - 1) << 4), (byte)(_PROGRESSIVE | 0x02 | (alpha ? 0x04 : 0)), 1 },
  });

  private static MediaStreamInfo _LegacyRequest(int width, int height, int bitsPerPixel) {
    var header = new byte[_BITMAP_INFO_HEADER_SIZE];
    BinaryPrimitives.WriteUInt32LittleEndian(header, _BITMAP_INFO_HEADER_SIZE);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)bitsPerPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), CodecTag.FromCharacters("HFYU").Value);
    return new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("HFYU"), Width = width, Height = height, BitsPerPixel = bitsPerPixel, CodecPrivateData = header };
  }

  private static MediaStreamInfo _Request(int width, int height, int bitsPerPixel, Rational? timeBase = null, Rational? frameRate = null) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Width = width, Height = height, BitsPerPixel = bitsPerPixel,
    TimeBase = timeBase ?? Rational.Unknown, FrameRate = frameRate ?? Rational.Unknown,
  };

  private static RawImage _Decode(MediaStreamInfo described, CodedPacket packet) {
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    return decoded;
  }

  private static RawImage _Random(PixelFormat format, int width, int height, int seed) {
    var prototype = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[checked((int)prototype.MinimumPixelDataLength)]; new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _RandomHighDepth(PixelFormat format, int width, int height, int bits, int seed) {
    var image = _Random(format, width, height, seed); var mask = (ushort)((1 << bits) - 1);
    for (var i = 0; i < image.PixelData.Length; i += 2) {
      var value = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(image.PixelData.AsSpan(i, 2)) & mask);
      BinaryPrimitives.WriteUInt16LittleEndian(image.PixelData.AsSpan(i, 2), value);
    }
    return image;
  }

  private static RawImage _Ramp16(PixelFormat format, int width, int height) {
    var image = _Random(format, width, height, 0);
    for (var i = 0; i < image.PixelData.Length / 2; ++i) BinaryPrimitives.WriteUInt16LittleEndian(image.PixelData.AsSpan(i * 2, 2), (ushort)(i * 257 + (i & 3)));
    return image;
  }

  private static RawImage _ValidHighPacked(PixelFormat format, int width, int height, int bits, int seed) {
    var image = _Random(format, width, height, seed);
    if (format == PixelFormat.Gray10)
      for (var i = 0; i < image.PixelData.Length; i += 2) BinaryPrimitives.WriteUInt16LittleEndian(image.PixelData.AsSpan(i, 2), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(image.PixelData.AsSpan(i, 2)) & 1023));
    else if (format == PixelFormat.Rgb30)
      for (var i = 0; i < image.PixelData.Length; i += 4) { var value = BinaryPrimitives.ReadUInt32LittleEndian(image.PixelData.AsSpan(i, 4)); value = (value & 0x3FFFFFFF) | 0xC0000000; BinaryPrimitives.WriteUInt32LittleEndian(image.PixelData.AsSpan(i, 4), value); }
    return image;
  }

  private static RawImage _Flat(PixelFormat format, int width, int height, byte value) { var image = _Random(format, width, height, 0); Array.Fill(image.PixelData, value); return image; }

  private static byte[] _Expected8(RawImage frame) {
    if (frame.Format == PixelFormat.Bgra32) return frame.ToRgba32();
    if (!frame.IsPlanarYuv) return frame.PixelData;
    var (sx, sy) = RawImage.YuvSubsampling(frame.Format); var (cw, _) = frame.GetPlaneDimensions(1);
    var yPlane = frame.GetPlaneData(0); var cb = frame.GetPlaneData(1); var cr = frame.GetPlaneData(2); var rgb = new byte[frame.Width * frame.Height * 3];
    for (var y = 0; y < frame.Height; ++y) for (var x = 0; x < frame.Width; ++x) {
      var c = (y / sy) * cw + x / sx; var scaled = 298 * (yPlane[y * frame.Width + x] - 16); var blue = cb[c] - 128; var red = cr[c] - 128; var at = (y * frame.Width + x) * 3;
      rgb[at] = _Clamp(scaled + 409 * red + 128); rgb[at + 1] = _Clamp(scaled - 100 * blue - 208 * red + 128); rgb[at + 2] = _Clamp(scaled + 516 * blue + 128);
    }
    return rgb;
  }

  private static byte _Clamp(int scaled) { var value = scaled >> 8; return (byte)(value < 0 ? 0 : value > 255 ? 255 : value); }
}
