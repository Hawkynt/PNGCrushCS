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
/// LCL ZLIB writer vectors: the conservative RGB default, every historical YUV layout, the modular
/// predictor and the two-zlib wrapper. Expected packed bytes are original hand-written vectors rather
/// than output copied from another implementation.
/// </summary>
[TestFixture]
public class LclZlibVideoEncoderTests {

  private static readonly RawImageColorInfo _YuvColorInfo = new() {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Bt601,
    ChromaLocation = RawChromaLocation.Unspecified,
  };

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

  private static byte[] _Inflate(ReadOnlySpan<byte> packet) {
    using var source = new MemoryStream(packet.ToArray());
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }

  private static (byte[] First, byte[] Second, int SectionLength) _InflateSplit(ReadOnlyMemory<byte> packet) {
    var span = packet.Span;
    var firstLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span));
    var sectionLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
    Assert.That(firstLength, Is.InRange(1, span.Length - 8));
    var first = _Inflate(span.Slice(8, firstLength));
    var second = _Inflate(span[(8 + firstLength)..]);
    return (first, second, sectionLength);
  }

  private static RawImage _Planar(int width, int height, PixelFormat format, int seed) {
    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(format);
    var chromaWidth = (width + subsampleX - 1) / subsampleX;
    var chromaHeight = (height + subsampleY - 1) / subsampleY;
    var pixels = new byte[checked(width * height + 2 * chromaWidth * chromaHeight)];
    new Random(seed).NextBytes(pixels);
    return new() {
      Width = width,
      Height = height,
      Format = format,
      PixelData = pixels,
      ColorInfo = _YuvColorInfo,
    };
  }

  [Test]
  [Category("Unit")]
  public void DescribesTheConservativeDefaultStream() {
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
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(20)), Is.EqualTo(13 * 7 * 3));
      Assert.That(format[40], Is.EqualTo(4));
      Assert.That(format[44], Is.EqualTo((byte)LclZlibVideoEncoder.ImageType.Rgb24));
      Assert.That(format[45], Is.EqualTo(6));
      Assert.That(format[46], Is.Zero);
      Assert.That(format[47], Is.EqualTo(3));
    });

    Assert.That(LclZlibVideoDecoder.Accepts(stream), Is.True);
    Assert.DoesNotThrow(() => LclZlibVideoDecoder.Create(stream));
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<LclZlibVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void HistoricalOptionsAreWrittenIntoTheTrailer() {
    var stream = LclZlibVideoEncoder.Create(
      _Stream(8, 6),
      LclZlibVideoEncoder.ImageType.Yuv420,
      pngFiltered: true,
      multithreaded: true).DescribeStream();
    var format = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(format[44], Is.EqualTo((byte)LclZlibVideoEncoder.ImageType.Yuv420));
      Assert.That(format[46], Is.EqualTo(0x05), "multithread + predictor");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(20)), Is.EqualTo(8 * 6 * 3 / 2));
      Assert.That(() => LclZlibVideoDecoder.Create(stream), Throws.Nothing);
    });
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
  public void DefaultRgbRoundTripsASequenceExactly(int width, int height, PixelFormat format) {
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
  public void HistoricalYuvModesRoundTripNativeSamplesForEveryFlagCombination() {
    var modes = new[] {
      // 6x3 is deliberately not section-aligned by row: the 54-byte YUV111 payload splits at byte 27.
      (LclZlibVideoEncoder.ImageType.Yuv111, PixelFormat.Yuv444P8, Width: 6, Height: 3),
      (LclZlibVideoEncoder.ImageType.Yuv422, PixelFormat.Yuv422P8, Width: 8, Height: 5),
      (LclZlibVideoEncoder.ImageType.Yuv411, PixelFormat.Yuv411P8, Width: 8, Height: 5),
      (LclZlibVideoEncoder.ImageType.Yuv211, PixelFormat.Yuv422P8, Width: 8, Height: 5),
      (LclZlibVideoEncoder.ImageType.Yuv420, PixelFormat.Yuv420P8, Width: 8, Height: 6),
    };

    foreach (var (imageType, format, width, height) in modes)
      foreach (var pngFiltered in new[] { false, true })
        foreach (var multithreaded in new[] { false, true }) {
          var picture = _Planar(width, height, format, seed: 1000 + (int)imageType * 17);
          var encoder = LclZlibVideoEncoder.Create(_Stream(width, height), imageType, pngFiltered, multithreaded);
          var decoder = LclZlibVideoDecoder.Create(encoder.DescribeStream());

          Assert.That(encoder.TryEncode(picture, 7, out var packet), Is.True, $"{imageType}, filter={pngFiltered}, split={multithreaded}");
          Assert.That(packet.IsKeyFrame, Is.True);
          Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
          Assert.Multiple(() => {
            Assert.That(decoded.Format, Is.EqualTo(format), imageType.ToString());
            Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), $"{imageType}, filter={pngFiltered}, split={multithreaded}");
          });
        }
  }

  [Test]
  [Category("Unit")]
  public void HistoricalRgbFlagsRoundTripWhenRowsNeedNoPadding() {
    var picture = LosslessEncoderPictures.Noise(8, 5, PixelFormat.Bgr24, seed: 44);

    foreach (var pngFiltered in new[] { false, true })
      foreach (var multithreaded in new[] { false, true }) {
        var encoder = LclZlibVideoEncoder.Create(
          _Stream(8, 5), LclZlibVideoEncoder.ImageType.Rgb24, pngFiltered, multithreaded);
        var decoder = LclZlibVideoDecoder.Create(encoder.DescribeStream());

        Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
        Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
        Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), $"filter={pngFiltered}, split={multithreaded}");
      }
  }

  [Test]
  [Category("Unit")]
  public void SplitRgbUsesHistoricalFourByteRowPadding() {
    var picture = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Bgr24,
      PixelData = new byte[] {
        10, 11, 12, 13, 14, 15, 16, 17, 18,
        1, 2, 3, 4, 5, 6, 7, 8, 9,
      },
    };
    var encoder = LclZlibVideoEncoder.Create(
      _Stream(3, 2), LclZlibVideoEncoder.ImageType.Rgb24, multithreaded: true);
    var decoder = LclZlibVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var (first, second, sectionLength) = _InflateSplit(packet.Data);
    var packed = first.Concat(second).ToArray();

    Assert.Multiple(() => {
      Assert.That(sectionLength, Is.EqualTo(12));
      Assert.That(packed, Is.EqualTo(new byte[] {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0,
        10, 11, 12, 13, 14, 15, 16, 17, 18, 0, 0, 0,
      }));
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void DefaultPacketsInflateToPackedBottomUpRgb() {
    var picture = LosslessEncoderPictures.Noise(13, 7, PixelFormat.Bgr24, seed: 3);
    var encoder = LclZlibVideoEncoder.Create(_Stream(13, 7));

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var inflated = _Inflate(packet.Data.Span);

    Assert.That(inflated, Has.Length.EqualTo(13 * 3 * 7));
    Assert.That(inflated.AsSpan(0, 39).ToArray(), Is.EqualTo(picture.PixelData.AsSpan(6 * 39, 39).ToArray()));
    Assert.That(inflated.AsSpan(6 * 39, 39).ToArray(), Is.EqualTo(picture.PixelData.AsSpan(0, 39).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void RgbPredictorWritesTheReferenceInverseBytes() {
    var picture = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Bgr24,
      PixelData = new byte[] { 10, 20, 30, 9, 19, 31 },
    };
    var encoder = LclZlibVideoEncoder.Create(
      _Stream(2, 1), LclZlibVideoEncoder.ImageType.Rgb24, pngFiltered: true);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(_Inflate(packet.Data.Span), Is.EqualTo(new byte[] { 10, 20, 30, 1, 1, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void Yuv420PredictorWritesEachComponentAccumulatorIndependently() {
    var picture = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = new byte[] { 3, 4, 1, 2, 133, 134 },
      ColorInfo = _YuvColorInfo,
    };
    var encoder = LclZlibVideoEncoder.Create(
      _Stream(2, 2), LclZlibVideoEncoder.ImageType.Yuv420, pngFiltered: true);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(_Inflate(packet.Data.Span), Is.EqualTo(new byte[] { 255, 255, 253, 255, 251, 250 }));
  }

  [Test]
  [Category("Unit")]
  public void MultithreadPacketContainsTwoIndependentEqualOutputStreams() {
    var picture = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = new byte[] {
        11, 12, 13, 14,
        1, 2, 3, 4,
        130, 131,
        126, 127,
      },
      ColorInfo = _YuvColorInfo,
    };
    var encoder = LclZlibVideoEncoder.Create(
      _Stream(4, 2), LclZlibVideoEncoder.ImageType.Yuv420, multithreaded: true);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var (first, second, sectionLength) = _InflateSplit(packet.Data);

    Assert.Multiple(() => {
      Assert.That(sectionLength, Is.EqualTo(6));
      Assert.That(first, Has.Length.EqualTo(sectionLength));
      Assert.That(second, Has.Length.EqualTo(sectionLength));
      Assert.That(first.Concat(second).ToArray(), Is.EqualTo(new byte[] {
        1, 2, 11, 12, 2, 254,
        3, 4, 13, 14, 3, 255,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void YuvModeConvertsEightBitRgbThroughLclsFullRangeColourSpace() {
    var source = LosslessEncoderPictures.Noise(8, 6, PixelFormat.Bgr24, seed: 19);
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8, _YuvColorInfo);
    var encoder = LclZlibVideoEncoder.Create(_Stream(8, 6), LclZlibVideoEncoder.ImageType.Yuv420);
    var decoder = LclZlibVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(reference.PixelData));
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
  public void MuxesHistoricalYuvIntoAviAndDecodesBackThroughTheContainer() {
    var picture = _Planar(8, 6, PixelFormat.Yuv420P8, seed: 91);
    var encoder = LclZlibVideoEncoder.Create(
      _Stream(8, 6),
      LclZlibVideoEncoder.ImageType.Yuv420,
      pngFiltered: true,
      multithreaded: true);
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoded = VideoIO.Decode(AviContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder).Single().Image;

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }
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
  public void RefusesAnUnknownHistoricalImageType() {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      LclZlibVideoEncoder.Create(_Stream(4, 4), (LclZlibVideoEncoder.ImageType)6));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void DefaultRgbRefusesInputThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = LclZlibVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void HistoricalYuvRefusesHighDepthInputRatherThanQuantising() {
    var encoder = LclZlibVideoEncoder.Create(_Stream(4, 4), LclZlibVideoEncoder.ImageType.Yuv420);
    var picture = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Yuv420P10,
      PixelData = new byte[4 * 4 * 3],
    };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("eight-bit"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesGeometryThatHistoricalPackingWouldLose() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoEncoder.Create(_Stream(6, 4), LclZlibVideoEncoder.ImageType.Yuv422));
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoEncoder.Create(_Stream(6, 4), LclZlibVideoEncoder.ImageType.Yuv411));
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoEncoder.Create(_Stream(3, 4), LclZlibVideoEncoder.ImageType.Yuv211));
      Assert.Throws<NotSupportedException>(() =>
        LclZlibVideoEncoder.Create(_Stream(4, 3), LclZlibVideoEncoder.ImageType.Yuv420));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddSizedTwoStreamPayload() {
    var failure = Assert.Throws<NotSupportedException>(() =>
      LclZlibVideoEncoder.Create(_Stream(1, 1), LclZlibVideoEncoder.ImageType.Yuv111, multithreaded: true));
    Assert.That(failure!.Message, Does.Contain("odd coded size"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesFilteredSplitRgbWhenPaddingWouldBreakReferenceDecoding() {
    var failure = Assert.Throws<NotSupportedException>(() =>
      LclZlibVideoEncoder.Create(
        _Stream(3, 2),
        LclZlibVideoEncoder.ImageType.Rgb24,
        pngFiltered: true,
        multithreaded: true));
    Assert.That(failure!.Message, Does.Contain("four-byte-padded"));
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
