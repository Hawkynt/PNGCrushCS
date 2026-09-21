using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>Writer compatibility modes which all decode through the same FFV1 predictor.</summary>
[TestFixture]
public class Ffv1WriterModesTests {

  [TestCase(0, Ffv1EntropyCoder.GolombRice, Ffv1ContextModel.Small)]
  [TestCase(0, Ffv1EntropyCoder.Range, Ffv1ContextModel.Large)]
  [TestCase(0, Ffv1EntropyCoder.RangeCustom, Ffv1ContextModel.Small)]
  [TestCase(1, Ffv1EntropyCoder.GolombRice, Ffv1ContextModel.Large)]
  [TestCase(1, Ffv1EntropyCoder.Range, Ffv1ContextModel.Small)]
  [TestCase(1, Ffv1EntropyCoder.RangeCustom, Ffv1ContextModel.Large)]
  [TestCase(3, Ffv1EntropyCoder.GolombRice, Ffv1ContextModel.Small)]
  [TestCase(3, Ffv1EntropyCoder.GolombRice, Ffv1ContextModel.Large)]
  [TestCase(3, Ffv1EntropyCoder.Range, Ffv1ContextModel.Small)]
  [TestCase(3, Ffv1EntropyCoder.Range, Ffv1ContextModel.Large)]
  [TestCase(3, Ffv1EntropyCoder.RangeCustom, Ffv1ContextModel.Small)]
  [TestCase(3, Ffv1EntropyCoder.RangeCustom, Ffv1ContextModel.Large)]
  [Category("Unit")]
  public void VersionsCodersAndContextModelsRoundTripExactly(
    int version, Ffv1EntropyCoder entropyCoder, Ffv1ContextModel contextModel) {
    var options = new Ffv1EncoderOptions {
      Version = version,
      EntropyCoder = entropyCoder,
      ContextModel = contextModel,
      KeyFrameInterval = 3,
      HorizontalSlices = version == 3 ? 1 : 0,
      VerticalSlices = version == 3 ? 1 : 0,
      StateTransitionDelta = entropyCoder == Ffv1EntropyCoder.RangeCustom ? _CustomTransitions() : null,
    };
    var encoder = Ffv1Encoder.Create(_Stream(17, 11), PixelFormat.Rgb24, options);
    var pictures = new[] { _Picture(17, 11, 13), _Picture(17, 11, 14), _Picture(17, 11, 15) };
    var packets = new CodedPacket[pictures.Length];

    for (var i = 0; i < pictures.Length; ++i)
      Assert.That(encoder.TryEncode(pictures[i], i, out packets[i]), Is.True);

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
      Assert.That(packets[2].IsKeyFrame, Is.False);
      Assert.That(encoder.DescribeStream().CodecPrivateData.IsEmpty, Is.EqualTo(version < 3));
    });

    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    for (var i = 0; i < packets.Length; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"version {version}, {entropyCoder}, {contextModel}, frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void GolombRunModeRoundTripsLongFlatRuns() {
    const int width = 127;
    const int height = 9;
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = (byte)(y is 3 or 7 && x > 93 ? x : 0);

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
    var encoder = Ffv1Encoder.Create(_Stream(width, height, 8), PixelFormat.Gray8, new() {
      EntropyCoder = Ffv1EntropyCoder.GolombRice,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Version3WithoutSliceCrcStillRoundTrips() {
    var source = _Picture(13, 7, 44);
    var encoder = Ffv1Encoder.Create(_Stream(13, 7), PixelFormat.Rgb24, new() {
      EntropyCoder = Ffv1EntropyCoder.Range,
      SliceCrc = false,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });

    encoder.TryEncode(source, 0, out var packet);
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void InvalidWriterModeCombinationsAreRejected() {
    var stream = _Stream(8, 8);

    Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(stream, PixelFormat.Gray8, new() {
      Version = 1,
      HorizontalSlices = 2,
      VerticalSlices = 2,
    }));
    Assert.Throws<ArgumentException>(() => Ffv1Encoder.Create(stream, PixelFormat.Gray8, new() {
      EntropyCoder = Ffv1EntropyCoder.RangeCustom,
    }));
    Assert.Throws<ArgumentException>(() => Ffv1Encoder.Create(stream, PixelFormat.Gray8, new() {
      EntropyCoder = Ffv1EntropyCoder.Range,
      StateTransitionDelta = new int[256],
    }));

    var tooManySlices = Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(64, 64), PixelFormat.Gray8, new() {
      HorizontalSlices = 33,
      VerticalSlices = 33,
    }));
    Assert.That(tooManySlices!.Message, Does.Contain("1024"));
  }

  private static int[] _CustomTransitions() {
    var result = new int[256];
    result[128] = 1;
    result[129] = -1;
    result[130] = 1;
    return result;
  }

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel = 24) => new() {
    Index = 6,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 30),
    FrameRate = new Rational(30, 1),
    BitsPerPixel = bitsPerPixel,
  };

  private static RawImage _Picture(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    var random = new Random(seed);
    random.NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
