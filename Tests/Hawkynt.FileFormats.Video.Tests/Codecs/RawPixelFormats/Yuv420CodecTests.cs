using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The five 4:2:0 codes — YV12, I420, IYUV, NV12 and NV21 — decoders and encoders together.
/// </summary>
/// <remarks>
/// One fixture for five codecs because the five are one sample grid under four arrangements of it,
/// and what has to be pinned down is exactly the difference between them. Each was measured against
/// ffmpeg over pseudo-random content at 16x8, 34x18 and 8x6, five frames apiece, and every sample of
/// every plane came back identical; a sample comparison cannot say which of the two chroma planes
/// comes first, which is what the hand-built frames below are for.
/// </remarks>
[TestFixture]
public class Yuv420CodecTests {

  /// <summary>A 2x2 picture: four luma samples and the one chroma pair that covers them.</summary>
  private static readonly byte[] _Planes = [10, 20, 30, 40, 90, 200];

  private static MediaStreamInfo _Stream(int width, int height, string tag, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Random(int width, int height, int seed) {
    var pixels = new byte[width * height * 3 / 2];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = pixels };
  }

  /// <summary>The bytes each code makes of <see cref="_Planes"/>.</summary>
  private static byte[] _Coded(string tag) => tag switch {
    "YV12" => [10, 20, 30, 40, 200, 90],
    "I420" or "IYUV" => [10, 20, 30, 40, 90, 200],
    "NV12" => [10, 20, 30, 40, 90, 200],
    _ => [10, 20, 30, 40, 200, 90],
  };

  private static (byte[] Luma, byte[] Cb, byte[] Cr) _DecodePlanes(string tag, MediaStreamInfo stream, byte[] data) => tag switch {
    "YV12" => Yv12VideoDecoder.Create(stream).DecodePlanes(data),
    "I420" => I420VideoDecoder.Create(stream).DecodePlanes(data),
    "IYUV" => IyuvVideoDecoder.Create(stream).DecodePlanes(data),
    "NV12" => Nv12VideoDecoder.Create(stream).DecodePlanes(data),
    _ => Nv21VideoDecoder.Create(stream).DecodePlanes(data),
  };

  // ============================================================================================
  // Which stream each code takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void EachCodeAcceptsItsOwnTagIgnoringCaseAndNoOther(string tag) {
    Assert.Multiple(() => {
      foreach (var other in new[] { "YV12", "I420", "IYUV", "NV12", "NV21" }) {
        var stream = _Stream(4, 2, other);
        var accepted = tag switch {
          "YV12" => Yv12VideoDecoder.Accepts(stream),
          "I420" => I420VideoDecoder.Accepts(stream),
          "IYUV" => IyuvVideoDecoder.Accepts(stream),
          "NV12" => Nv12VideoDecoder.Accepts(stream),
          _ => Nv21VideoDecoder.Accepts(stream),
        };
        Assert.That(accepted, Is.EqualTo(tag == other), $"{tag} decoder against a {other} stream");
      }

      Assert.That(Nv12VideoDecoder.Accepts(_Stream(4, 2, "nv12")), Is.True, "the tag is matched ignoring case");
    });
  }

  // ============================================================================================
  // The packing itself
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void UnpacksOneTwoByTwoFrameIntoItsThreePlanes(string tag) {
    var (luma, cb, cr) = _DecodePlanes(tag, _Stream(2, 2, tag), _Coded(tag));

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new byte[] { 10, 20, 30, 40 }), "luma");
      Assert.That(cb, Is.EqualTo(new byte[] { 90 }), "cb");
      Assert.That(cr, Is.EqualTo(new byte[] { 200 }), "cr");
    });
  }

  /// <summary>
  /// The pair that differs only in chroma order really does differ in chroma order, and in nothing
  /// else — the failure mode of these codes is a picture that decodes and has its reds and blues
  /// exchanged, which no sample count against a single reference ever catches.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCase("I420", "YV12")]
  [TestCase("NV12", "NV21")]
  public void TheChromaExchangedPairReadTheSameBytesWithTheChromaPlanesExchanged(string one, string other) {
    var data = _Coded(one);

    var first = _DecodePlanes(one, _Stream(2, 2, one), data);
    var second = _DecodePlanes(other, _Stream(2, 2, other), data);

    Assert.Multiple(() => {
      Assert.That(second.Luma, Is.EqualTo(first.Luma), "luma is the same either way");
      Assert.That(second.Cb, Is.EqualTo(first.Cr), "one code's Cb is the other's Cr");
      Assert.That(second.Cr, Is.EqualTo(first.Cb), "and the other way round");
    });
  }

  /// <summary>I420 and IYUV are one layout under two names, and have to stay one layout.</summary>
  [Test]
  [Category("Unit")]
  public void I420AndIyuvCodeTheSameBytes() {
    var source = _Random(8, 6, 31);
    var i420 = VideoFormatRegistry.CreateEncoder(_Stream(8, 6, "I420"));
    var iyuv = VideoFormatRegistry.CreateEncoder(_Stream(8, 6, "IYUV"));

    Assert.That(i420.TryEncode(source, 0, out var one), Is.True);
    Assert.That(iyuv.TryEncode(source, 0, out var other), Is.True);

    Assert.That(other.Data.ToArray(), Is.EqualTo(one.Data.ToArray()));
  }

  /// <summary>The interleaved plane really is interleaved, and not two half planes in a row.</summary>
  [Test]
  [Category("Unit")]
  public void TheSemiPlanarCodesInterleaveTheChromaBytesPairByPair() {
    var source = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = [1, 2, 3, 4, 5, 6, 7, 8, 100, 101, 200, 201],
    };
    var nv12 = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "NV12"));
    var nv21 = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "NV21"));

    Assert.That(nv12.TryEncode(source, 0, out var twelve), Is.True);
    Assert.That(nv21.TryEncode(source, 0, out var twentyOne), Is.True);

    Assert.Multiple(() => {
      Assert.That(twelve.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 100, 200, 101, 201 }));
      Assert.That(twentyOne.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 200, 100, 201, 101 }));
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void PacksTheSameBytesBackFromOneTwoByTwoFrame(string tag) {
    var frame = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Yuv420P8, PixelData = _Planes };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(2, 2, tag));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(_Coded(tag)));
  }

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void AFrameIsExactlyWidthTimesHeightTimesThreeHalvesWithNoPaddingAtAll(string tag) {
    var stream = _Stream(34, 18, tag);

    Assert.That(() => _DecodePlanes(tag, stream, new byte[34 * 18 * 3 / 2]), Throws.Nothing);
    Assert.That(() => _DecodePlanes(tag, stream, new byte[34 * 18 * 3 / 2 - 1]), Throws.InstanceOf<InvalidDataException>());
  }

  /// <summary>
  /// A source carrying more chroma than 4:2:0 holds keeps its luma exactly and has its chroma
  /// averaged onto the two-by-two grid, so one already sited there is reproduced exactly.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ChromaAlreadySitedAtOnePairPerBlockSurvivesA444Source() {
    var frame = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv444P8,
      PixelData = [10, 20, 30, 40, 90, 90, 90, 90, 200, 200, 200, 200],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(2, 2, "I420"));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(_Planes));
  }

  /// <summary>And where it carries more, the pair that represents a block is the mean of it.</summary>
  [Test]
  [Category("Unit")]
  public void ChromaThatVariesWithinABlockIsAveragedRatherThanDropped() {
    var frame = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv444P8,
      PixelData = [10, 20, 30, 40, 10, 20, 30, 41, 100, 100, 100, 100],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(2, 2, "I420"));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    // (10 + 20 + 30 + 41 + 2) / 4 == 25, rounded to nearest rather than truncated.
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30, 40, 25, 100 }));
  }

  // ============================================================================================
  // Round trips
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YV12", 16, 8)]
  [TestCase("I420", 34, 18)]
  [TestCase("IYUV", 8, 6)]
  [TestCase("NV12", 16, 8)]
  [TestCase("NV21", 34, 18)]
  [TestCase("NV12", 2, 2)]
  public void RoundTripsPseudoRandomSamplesThroughTheRegistryDecoder(string tag, int width, int height) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(width, height, tag));
    var described = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(described);

    for (var i = 0; i < 3; ++i) {
      var source = _Random(width, height, 3000 + i);
      Assert.That(encoder.TryEncode(source, i, out var packet), Is.True);
      Assert.That(packet.Data.Length, Is.EqualTo(width * height * 3 / 2));
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData), "every sample of every plane");
      });
    }
  }

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void DescribesAStreamTheDecoderAccepts(string tag) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(16, 8, tag, 3));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters(tag)));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(3));
      Assert.That(described.Width, Is.EqualTo(16));
      Assert.That(described.Height, Is.EqualTo(8));
      Assert.That(described.BitsPerPixel, Is.EqualTo(12));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  // ============================================================================================
  // Geometry
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void RefusesAnOddWidthOrAnOddHeightInBothDirections(string tag) {
    Assert.Multiple(() => {
      foreach (var (width, height) in new[] { (7, 8), (8, 7), (7, 7) }) {
        var stream = _Stream(width, height, tag);
        Assert.That(
          Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateDecoder(stream))!.Message,
          Does.Contain($"{width}x{height}"), $"decoding {width}x{height}");
        Assert.That(
          Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateEncoder(stream))!.Message,
          Does.Contain($"{width}x{height}"), $"encoding {width}x{height}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase("YV12")]
  [TestCase("I420")]
  [TestCase("IYUV")]
  [TestCase("NV12")]
  [TestCase("NV21")]
  public void RefusesAPictureWithNoPixels(string tag) {
    var failure = Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.CreateDecoder(_Stream(0, 4, tag)));

    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "I420"));
    Assert.That(encoder.TryEncode(_Random(4, 2, 9), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Random(4, 4, 10), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("4x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithTooLittlePixelData() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "NV21"));
    var truncated = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Yuv420P8, PixelData = new byte[11] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(truncated, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamOfAnotherKind() {
    Assert.Throws<NotSupportedException>(
      () => Yv12VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("YV12"), Width = 4, Height = 4 }));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "NV12"));

    Assert.That(encoder.TryEncode(_Random(4, 2, 13), 42, out var packet), Is.True);
    Assert.That(encoder.TryEncode(_Random(4, 2, 14), null, out var untimed), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(untimed.PresentationTimestamp, Is.Null);
      Assert.That(untimed.IsKeyFrame, Is.True);
      Assert.That(encoder.Flush(), Is.Empty);
    });
  }
}
