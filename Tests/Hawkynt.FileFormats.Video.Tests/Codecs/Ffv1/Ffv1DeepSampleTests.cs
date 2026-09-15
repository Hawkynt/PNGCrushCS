using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>FFV1 integer sample depths above eight bits, including the historical transform quirks.</summary>
[TestFixture]
public class Ffv1DeepSampleTests {

  [TestCase(9)]
  [TestCase(10)]
  [TestCase(11)]
  [TestCase(12)]
  [TestCase(13)]
  [TestCase(14)]
  [TestCase(15)]
  [TestCase(16)]
  [Category("Unit")]
  public void GreyNineThroughSixteenBitsRoundTripExactly(int bits) {
    var format = bits <= 10 ? PixelFormat.Gray10 : PixelFormat.Gray16;
    var source = _WordPicture(19, 11, format, bits, 1, bits * 31);
    var encoder = Ffv1Encoder.Create(_Stream(19, 11), format, new() {
      BitsPerRawSample = bits,
      EntropyCoder = Ffv1EntropyCoder.Range,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    encoder.TryEncode(source, 0, out var packet);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(bits <= 10 ? PixelFormat.Gray10 : PixelFormat.Gray16));
      Assert.That(_Samples(decoded, 1), Is.EqualTo(_Samples(source, 1)));
    });
  }

  [TestCase(9, PixelFormat.Yuv420P10)]
  [TestCase(10, PixelFormat.Yuv422P10)]
  [TestCase(11, PixelFormat.Yuv440P12)]
  [TestCase(12, PixelFormat.Yuv444P12)]
  [TestCase(13, PixelFormat.Yuv420P16)]
  [TestCase(14, PixelFormat.Yuv422P16)]
  [TestCase(15, PixelFormat.Yuv440P16)]
  [TestCase(16, PixelFormat.Yuv444P16)]
  [Category("Unit")]
  public void PlanarYuvNineThroughSixteenBitsRoundTripExactly(int bits, PixelFormat format) {
    const int width = 18;
    const int height = 10;
    var source = _YuvPicture(width, height, format, bits, bits * 43);
    var encoder = Ffv1Encoder.Create(_Stream(width, height), format, new() {
      BitsPerRawSample = bits,
      EntropyCoder = Ffv1EntropyCoder.Range,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    encoder.TryEncode(source, 0, out var packet);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(_Samples(decoded, 3), Is.EqualTo(_Samples(source, 3)));
  }

  [TestCase(9)]
  [TestCase(10)]
  [TestCase(12)]
  [TestCase(14)]
  [TestCase(15)]
  [TestCase(16)]
  [Category("Unit")]
  public void DeepRgbRoundTripsAcrossLegacyAndCurrentColourTransforms(int bits) {
    var source = _WordPicture(13, 9, PixelFormat.Rgb48, bits, 3, bits * 71);
    var encoder = Ffv1Encoder.Create(_Stream(13, 9), PixelFormat.Rgb48, new() {
      BitsPerRawSample = bits,
      EntropyCoder = Ffv1EntropyCoder.Range,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    encoder.TryEncode(source, 0, out var packet);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb48));
      Assert.That(_Samples(decoded, 3), Is.EqualTo(_Samples(source, 3)));
    });
  }

  [TestCase(10)]
  [TestCase(12)]
  [TestCase(16)]
  [Category("Unit")]
  public void DeepRgbaRoundTripsWithAlpha(int bits) {
    var source = _WordPicture(11, 7, PixelFormat.Rgba64, bits, 4, bits * 97);
    var encoder = Ffv1Encoder.Create(_Stream(11, 7), PixelFormat.Rgba64, new() {
      BitsPerRawSample = bits,
      EntropyCoder = Ffv1EntropyCoder.Range,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    encoder.TryEncode(source, 0, out var packet);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(_Samples(decoded, 4), Is.EqualTo(_Samples(source, 4)));
  }

  [Test]
  [Category("Unit")]
  public void VersionOneCarriesDeepSampleWidthInItsKeyframe() {
    var source = _YuvPicture(16, 8, PixelFormat.Yuv420P12, 12, 1201);
    var encoder = Ffv1Encoder.Create(_Stream(16, 8), PixelFormat.Yuv420P12, new() {
      Version = 1,
      BitsPerRawSample = 12,
      EntropyCoder = Ffv1EntropyCoder.Range,
      KeyFrameInterval = 2,
    });

    encoder.TryEncode(source, 0, out var key);
    encoder.TryEncode(source, 1, out var dependent);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());

    Assert.That(decoder.TryDecode(key, out var first), Is.True);
    Assert.That(decoder.TryDecode(dependent, out var second), Is.True);
    Assert.Multiple(() => {
      Assert.That(_Samples(first, 3), Is.EqualTo(_Samples(source, 3)));
      Assert.That(_Samples(second, 3), Is.EqualTo(_Samples(source, 3)));
    });
  }

  [Test]
  [Category("Unit")]
  public void HighBitContextTablesUseThePublishedContextCounts() {
    var small = _Parameters(Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray16, new() {
      BitsPerRawSample = 16,
      ContextModel = Ffv1ContextModel.Small,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    }).DescribeStream());
    var large = _Parameters(Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray16, new() {
      BitsPerRawSample = 16,
      ContextModel = Ffv1ContextModel.Large,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    }).DescribeStream());

    Assert.Multiple(() => {
      Assert.That(small.ContextCount[0], Is.EqualTo(365));
      Assert.That(large.ContextCount[0], Is.EqualTo(5063));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASourceSampleOutsideTheDeclaredBitDepthIsRefused() {
    var source = _WordPicture(4, 4, PixelFormat.Gray16, 10, 1, 4);
    BinaryPrimitives.WriteUInt16BigEndian(source.PixelData.AsSpan(0, 2), 1024);
    var encoder = Ffv1Encoder.Create(_Stream(4, 4), PixelFormat.Gray16, new() {
      BitsPerRawSample = 10,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(source, 0, out _));
    Assert.That(failure!.Message, Does.Contain("outside the 0..1023 range"));
  }

  [Test]
  [Category("Unit")]
  public void DeepGolombOutputIsRefusedForInteroperability() {
    Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray16, new() {
      BitsPerRawSample = 12,
      EntropyCoder = Ffv1EntropyCoder.GolombRice,
    }));
  }

  private static Ffv1Parameters _Parameters(MediaStreamInfo stream) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return Ffv1Parameters.Read(new Ffv1RangeCoder(stream.CodecPrivateData[..^4], zero, one), states, true);
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 9,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _WordPicture(int width, int height, PixelFormat format, int bits, int channels, int seed) {
    var count = width * height * channels;
    var data = new byte[count * 2];
    var mask = (1 << bits) - 1;
    var random = new Random(seed);
    var littleEndian = format == PixelFormat.Gray10;
    for (var i = 0; i < count; ++i) {
      var value = (ushort)random.Next(mask + 1);
      if (littleEndian)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2, 2), value);
      else
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2, 2), value);
    }

    return new() { Width = width, Height = height, Format = format, PixelData = data };
  }

  private static RawImage _YuvPicture(int width, int height, PixelFormat format, int bits, int seed) {
    var probe = new RawImage { Width = width, Height = height, Format = format, PixelData = new byte[(int)_MinimumLength(width, height, format)] };
    var data = probe.PixelData;
    var mask = (1 << bits) - 1;
    var random = new Random(seed);
    for (var offset = 0; offset < data.Length; offset += 2)
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)random.Next(mask + 1));

    return probe;
  }

  private static long _MinimumLength(int width, int height, PixelFormat format) {
    var probe = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    return probe.MinimumPixelDataLength;
  }

  private static int[] _Samples(RawImage image, int channelsOrPlanes) {
    if (image.Format is PixelFormat.Gray10
        or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10
        or PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12
        or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16) {
      var result = new int[image.PixelData.Length / 2];
      for (var i = 0; i < result.Length; ++i)
        result[i] = BinaryPrimitives.ReadUInt16LittleEndian(image.PixelData.AsSpan(i * 2, 2));
      return result;
    }

    if (channelsOrPlanes > 0) {
      var result = new int[image.PixelData.Length / 2];
      for (var i = 0; i < result.Length; ++i)
        result[i] = BinaryPrimitives.ReadUInt16BigEndian(image.PixelData.AsSpan(i * 2, 2));
      return result;
    }

    throw new InvalidOperationException();
  }
}
