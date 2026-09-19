using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.HuffYuv.Tests;

[TestFixture]
public class HuffYuvDecoderTests {

  private const int _BITMAP_INFO_HEADER_SIZE = 40;
  private const byte _LEFT = 0, _GRADIENT = 1, _MEDIAN = 2;
  private const byte _DECORRELATE = 0x40, _PROGRESSIVE = 0x20, _INTERLACED = 0x10, _TABLES_PER_FRAME = 0x40;
  private const byte _CHROMA = 0x01, _PLANAR_RGB = 0x02, _ALPHA = 0x04;

  [Test, Category("Unit")]
  public void CodesAreAssignedFromTheLongestLengthDownAndNotTheShortestUp() {
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1, table: HuffYuvTestStream.TableOfLengths(1, 2, 2));
    Assert.That(_Decode(stream, new HuffYuvTestStream().Code("1 00 01 1").End()).PixelData, Is.EqualTo(new byte[] { 0, 1, 3, 3 }));
  }

  [Test, Category("Unit")]
  public void RunLengthEscapesAndWordSwappingAreBothObserved() {
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1);
    Assert.That(_Decode(stream, [4, 3, 2, 1]).PixelData, Is.EqualTo(new byte[] { 1, 3, 6, 10 }));
  }

  [Test, Category("Unit")]
  public void InvalidHuffmanLengthsAreRefused() {
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1, table: HuffYuvTestStream.TableOfLengths(1, 2));
    Assert.That(() => HuffYuvDecoder.Create(stream), Throws.TypeOf<InvalidDataException>().With.Message.Contains("complete code"));
  }

  [Test, Category("Unit")]
  public void LeftGradientAndMedianPredictionUseReferenceArithmetic() {
    var left = _Decode(HuffYuvTestStream.PlanarStream(3, 2, _LEFT, _PROGRESSIVE, 1), new HuffYuvTestStream().Symbols(10, 5, 5, 1, 1, 1).End());
    var gradient = _Decode(HuffYuvTestStream.PlanarStream(3, 2, _GRADIENT, _PROGRESSIVE, 1), new HuffYuvTestStream().Symbols(10, 5, 5, 1, 0, 0).End());
    var median = _Decode(HuffYuvTestStream.PlanarStream(3, 2, _MEDIAN, _PROGRESSIVE, 1), new HuffYuvTestStream().Symbols(10, 10, 10, 0, 0, 0).End());
    Assert.Multiple(() => {
      Assert.That(left.PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 21, 22, 23 }));
      Assert.That(gradient.PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 31, 36, 41 }));
      Assert.That(median.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 30, 30, 30 }));
    });
  }

  [Test, Category("Unit")]
  public void PredictionWrapsModuloEightBits() {
    var stream = HuffYuvTestStream.PlanarStream(3, 1, _LEFT, _PROGRESSIVE, 1);
    Assert.That(_Decode(stream, new HuffYuvTestStream().Symbols(200, 100, 212).End()).PixelData, Is.EqualTo(new byte[] { 200, 44, 0 }));
  }

  [Test, Category("Unit")]
  public void InterlacedPredictionTakesAboveFromTwoRowsBack() {
    var stream = HuffYuvTestStream.PlanarStream(2, 4, _GRADIENT, _INTERLACED, 1);
    Assert.That(_Decode(stream, new HuffYuvTestStream().Symbols(10, 0, 20, 0, 1, 0, 1, 0).End()).PixelData, Is.EqualTo(new byte[] { 10, 10, 30, 30, 41, 41, 62, 62 }));
  }

  [Test, Category("Unit")]
  public void PlanarRgbIsGreenBlueRedAndOptionalAlphaLast() {
    var rgb = _Decode(HuffYuvTestStream.PlanarStream(1, 1, _LEFT, (byte)(_PROGRESSIVE | _PLANAR_RGB), 3), new HuffYuvTestStream().Symbols(2, 3, 1).End());
    var rgba = _Decode(HuffYuvTestStream.PlanarStream(1, 1, _LEFT, (byte)(_PROGRESSIVE | _PLANAR_RGB | _ALPHA), 4), new HuffYuvTestStream().Symbols(2, 3, 1, 200).End());
    Assert.Multiple(() => {
      Assert.That(rgb.PixelData, Is.EqualTo(new byte[] { 1, 2, 3 }));
      Assert.That(rgba.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 200 }));
    });
  }

  [Test, Category("Unit")]
  public void PackedColourIsBottomUpAndRawPixelIsNotPredecorrelated() {
    var bottomUp = _Decode(HuffYuvTestStream.InterleavedStream(1, 2, _LEFT, 24, _PROGRESSIVE), new HuffYuvTestStream().Symbols(10, 20, 30, 0).Symbols(1, 2, 3).End());
    var decorrelated = _Decode(HuffYuvTestStream.InterleavedStream(1, 1, (byte)(_LEFT | _DECORRELATE), 24, _PROGRESSIVE), new HuffYuvTestStream().Symbols(0, 61, 103, 0).End());
    Assert.Multiple(() => {
      Assert.That(bottomUp.PixelData[3..], Is.EqualTo(new byte[] { 10, 20, 30 }));
      Assert.That(decorrelated.PixelData, Is.EqualTo(new byte[] { 0, 61, 103 }));
    });
  }

  [Test, Category("Unit")]
  public void PerFrameTablesAreInsideWordSwap() {
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, (byte)(_PROGRESSIVE | _TABLES_PER_FRAME), 1);
    var builder = new HuffYuvTestStream();
    foreach (var b in HuffYuvTestStream.FlatTable()) builder.Bits(b, 8);
    Assert.That(_Decode(stream, builder.Symbols(10, 5, 5, 5).End()).PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 25 }));
  }

  [Test, Category("Unit")]
  public void CodecPrivateDataMayBeRawDescriptionWithoutBitmapHeader() {
    var full = HuffYuvTestStream.Description(_LEFT, 0x70, _PROGRESSIVE, 1, 1);
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FFVH"), Width = 4, Height = 1, BitsPerPixel = 8, CodecPrivateData = full[_BITMAP_INFO_HEADER_SIZE..] };
    Assert.That(_Decode(stream, new HuffYuvTestStream().Symbols(10, 5, 5, 5).End()).PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 25 }));
  }

  [Test, Category("Unit")]
  public void InterlacedFourTwoZeroMedianIsDecodedRatherThanRefused() {
    var source = _RandomYuv420(16, 8, 4411);
    var encoder = HuffYuvEncoder.Create(_Request(16, 8, 12), HuffYuvPredictionMethod.Median, interlaced: true);
    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);
    var decoder = HuffYuvDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(_YuvToRgb(source)));
  }

  [Test, Category("Unit")]
  public void HeaderOnlyClassicDescriptionIsAcceptedAndUsesLowModeBits() {
    var request = _LegacyRequest(12, 6, 16 | 1);
    var source = _RandomYuv422(12, 6, 91);
    var encoder = HuffYuvEncoder.Create(request);
    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);
    var described = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(described.CodecPrivateData.Length, Is.EqualTo(_BITMAP_INFO_HEADER_SIZE));
      Assert.That(() => HuffYuvDecoder.Create(described), Throws.Nothing);
      Assert.That(_Decode(described, packet).PixelData, Is.EqualTo(_Yuv422ToRgb(source)));
    });
  }

  [Test, Category("Unit")]
  public void HeaderlessInterlaceFallbackUsesHeightAbove288() {
    var request = _LegacyRequest(8, 290, 16 | 3);
    var source = _RandomYuv422(8, 290, 92);
    var encoder = HuffYuvEncoder.Create(request);
    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);
    Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(_Yuv422ToRgb(source)));
  }

  [Test, Category("Unit")]
  public void UnstatedVersionTwoInterlaceAlsoUsesHistoricalHeightFallback() {
    var stream = HuffYuvTestStream.PlanarStream(4, 4, _LEFT, 0, 1);
    Assert.That(() => HuffYuvDecoder.Create(stream), Throws.Nothing);
  }

  [Test, Category("Unit")]
  public void UnsupportedLayoutsAreStillRefusedByName() {
    Assert.Multiple(() => {
      Assert.That(() => HuffYuvDecoder.Create(HuffYuvTestStream.InterleavedStream(8, 8, _MEDIAN, 24, _PROGRESSIVE)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("packed colour"));
      Assert.That(() => HuffYuvDecoder.Create(HuffYuvTestStream.InterleavedStream(2, 4, _MEDIAN, 16, _PROGRESSIVE)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("too small"));
      Assert.That(() => HuffYuvDecoder.Create(new MediaStreamInfo {
        Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FFVH"), Width = 4, Height = 4, BitsPerPixel = 16,
        CodecPrivateData = new byte[] { 0, 0x81, (byte)(_PROGRESSIVE | _CHROMA), 1 },
      }), Throws.TypeOf<NotSupportedException>().With.Message.Contains("9-bit"));
    });
  }

  [Test, Category("Unit")]
  public void CodecAnswersToBothFourccSpellingsOnlyForVideo() {
    foreach (var code in new[] { "HFYU", "FFVH", "hfyu" }) Assert.That(HuffYuvDecoder.Accepts(_Request(4, 4, 16, CodecTag.FromCharacters(code))), Is.True, code);
    Assert.That(HuffYuvDecoder.Accepts(_Request(4, 4, 16, CodecTag.FromCharacters("FFV1"))), Is.False);
  }

  private static RawImage _Decode(MediaStreamInfo stream, byte[] frame) => _Decode(stream, new CodedPacket(0, frame));

  private static RawImage _Decode(MediaStreamInfo stream, CodedPacket packet) {
    var decoder = HuffYuvDecoder.Create(stream); Assert.That(decoder.TryDecode(packet, out var picture), Is.True); return picture;
  }

  private static MediaStreamInfo _Request(int width, int height, int bpp, CodecTag codec = default) => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = codec, Width = width, Height = height, BitsPerPixel = bpp };

  private static MediaStreamInfo _LegacyRequest(int width, int height, int bpp) {
    var header = new byte[_BITMAP_INFO_HEADER_SIZE];
    BinaryPrimitives.WriteUInt32LittleEndian(header, _BITMAP_INFO_HEADER_SIZE); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)bpp); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), CodecTag.FromCharacters("HFYU").Value);
    return new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("HFYU"), Width = width, Height = height, BitsPerPixel = bpp, CodecPrivateData = header };
  }

  private static RawImage _RandomYuv420(int width, int height, int seed) => _Random(PixelFormat.Yuv420P8, width, height, seed);
  private static RawImage _RandomYuv422(int width, int height, int seed) => _Random(PixelFormat.Yuv422P8, width, height, seed);
  private static RawImage _Random(PixelFormat format, int width, int height, int seed) {
    var prototype = new RawImage { Width = width, Height = height, Format = format, PixelData = [] }; var bytes = new byte[checked((int)prototype.MinimumPixelDataLength)]; new Random(seed).NextBytes(bytes);
    return new() { Width = width, Height = height, Format = format, PixelData = bytes };
  }

  private static byte[] _YuvToRgb(RawImage frame) => _YuvToRgb(frame, 2, 2);
  private static byte[] _Yuv422ToRgb(RawImage frame) => _YuvToRgb(frame, 2, 1);
  private static byte[] _YuvToRgb(RawImage frame, int sx, int sy) {
    var yPlane = frame.GetPlaneData(0); var cb = frame.GetPlaneData(1); var cr = frame.GetPlaneData(2); var (cw, _) = frame.GetPlaneDimensions(1); var result = new byte[frame.Width * frame.Height * 3];
    for (var y = 0; y < frame.Height; ++y) for (var x = 0; x < frame.Width; ++x) {
      var c = (y / sy) * cw + x / sx; var scaled = 298 * (yPlane[y * frame.Width + x] - 16); var blue = cb[c] - 128; var red = cr[c] - 128; var at = (y * frame.Width + x) * 3;
      result[at] = _Clamp(scaled + 409 * red + 128); result[at + 1] = _Clamp(scaled - 100 * blue - 208 * red + 128); result[at + 2] = _Clamp(scaled + 516 * blue + 128);
    }
    return result;
  }

  private static byte _Clamp(int scaled) { var value = scaled >> 8; return (byte)(value < 0 ? 0 : value > 255 ? 255 : value); }
}
