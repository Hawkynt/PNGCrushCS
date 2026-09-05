using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The four packed 4:2:2 codes — YUY2, YVYU, UYVY and VYUY — decoders and encoders together.
/// </summary>
/// <remarks>
/// One fixture for four codecs because the four are one packing under four orderings of the same
/// four bytes, and what has to be pinned down is exactly the difference between them. Each was
/// measured against ffmpeg over pseudo-random content at 16x8, 34x18 and 8x5, five frames apiece, and
/// every sample of every plane came back identical; a sample comparison cannot say which byte of a
/// macropixel is which sample, which is what the hand-built four bytes below are for.
/// <para/>
/// VYUY is the one ffmpeg cannot answer for — its raw-video tag table maps that code onto
/// <c>yuyv422</c> — so its byte order comes from the Linux kernel's V4L2 documentation of
/// <c>V4L2_PIX_FMT_VYUY</c> and from Microsoft's description of it as UYVY with the chroma samples
/// exchanged. The test that reads the same four bytes through UYVY and through VYUY is what holds
/// that statement in place.
/// </remarks>
[TestFixture]
public class PackedYuv422CodecTests {

  /// <summary>Two pixels of known samples: Y0 81, Y1 235, Cb 90, Cr 240.</summary>
  private const byte _Y0 = 81;
  private const byte _Y1 = 235;
  private const byte _CB = 90;
  private const byte _CR = 240;

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
    var pixels = new byte[width * height * 2];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = pixels };
  }

  /// <summary>The four bytes each code makes of one macropixel of the samples above.</summary>
  private static byte[] _Macropixel(string tag) => tag switch {
    "YUY2" => [_Y0, _CB, _Y1, _CR],
    "YVYU" => [_Y0, _CR, _Y1, _CB],
    "UYVY" => [_CB, _Y0, _CR, _Y1],
    _ => [_CR, _Y0, _CB, _Y1],
  };

  private static (byte[] Luma, byte[] Cb, byte[] Cr) _DecodePlanes(string tag, MediaStreamInfo stream, byte[] data) => tag switch {
    "YUY2" => Yuy2VideoDecoder.Create(stream).DecodePlanes(data),
    "YVYU" => YvyuVideoDecoder.Create(stream).DecodePlanes(data),
    "UYVY" => UyvyVideoDecoder.Create(stream).DecodePlanes(data),
    _ => VyuyVideoDecoder.Create(stream).DecodePlanes(data),
  };

  // ============================================================================================
  // Which stream each code takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void EachCodeAcceptsItsOwnTagIgnoringCaseAndNoOther(string tag) {
    Assert.Multiple(() => {
      foreach (var other in new[] { "YUY2", "YVYU", "UYVY", "VYUY" }) {
        var stream = _Stream(4, 2, other);
        var accepted = tag switch {
          "YUY2" => Yuy2VideoDecoder.Accepts(stream),
          "YVYU" => YvyuVideoDecoder.Accepts(stream),
          "UYVY" => UyvyVideoDecoder.Accepts(stream),
          _ => VyuyVideoDecoder.Accepts(stream),
        };
        Assert.That(accepted, Is.EqualTo(tag == other), $"{tag} decoder against a {other} stream");
      }

      Assert.That(Yuy2VideoDecoder.Accepts(_Stream(4, 2, "yuy2")), Is.True, "the tag is matched ignoring case");
    });
  }

  // ============================================================================================
  // The packing itself
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void UnpacksTheFourBytesOfOneMacropixelIntoItsFourSamples(string tag) {
    var (luma, cb, cr) = _DecodePlanes(tag, _Stream(2, 1, tag), _Macropixel(tag));

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new[] { _Y0, _Y1 }), "luma");
      Assert.That(cb, Is.EqualTo(new[] { _CB }), "cb");
      Assert.That(cr, Is.EqualTo(new[] { _CR }), "cr");
    });
  }

  /// <summary>
  /// The pair that differs only in chroma order really does differ in chroma order, and in nothing
  /// else — the failure mode of all four of these codes is a picture that decodes and has its reds and
  /// blues exchanged, which no sample count against a single reference ever catches.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCase("YUY2", "YVYU")]
  [TestCase("UYVY", "VYUY")]
  public void TheChromaExchangedPairReadTheSameBytesWithTheChromaPlanesExchanged(string one, string other) {
    var data = _Macropixel(one);

    var first = _DecodePlanes(one, _Stream(2, 1, one), data);
    var second = _DecodePlanes(other, _Stream(2, 1, other), data);

    Assert.Multiple(() => {
      Assert.That(second.Luma, Is.EqualTo(first.Luma), "luma is the same either way");
      Assert.That(second.Cb, Is.EqualTo(first.Cr), "one code's Cb is the other's Cr");
      Assert.That(second.Cr, Is.EqualTo(first.Cb), "and the other way round");
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void PacksTheSameFourBytesBackFromOneMacropixel(string tag) {
    var frame = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Yuv422P8,
      PixelData = [_Y0, _Y1, _CB, _CR],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(2, 1, tag));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(_Macropixel(tag)));
  }

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void ARowIsExactlyWidthTimesTwoBytesWithNoPaddingAtAll(string tag) {
    var stream = _Stream(34, 3, tag);

    Assert.That(() => _DecodePlanes(tag, stream, new byte[34 * 2 * 3]), Throws.Nothing);
    Assert.That(() => _DecodePlanes(tag, stream, new byte[34 * 2 * 3 - 1]), Throws.InstanceOf<InvalidDataException>());
  }

  // ============================================================================================
  // Round trips
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YUY2", 16, 8)]
  [TestCase("YVYU", 34, 18)]
  [TestCase("UYVY", 8, 5)]
  [TestCase("VYUY", 8, 5)]
  [TestCase("YUY2", 2, 1)]
  public void RoundTripsPseudoRandomSamplesThroughTheRegistryDecoder(string tag, int width, int height) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(width, height, tag));
    var described = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(described);

    for (var i = 0; i < 3; ++i) {
      var source = _Random(width, height, 2000 + i);
      Assert.That(encoder.TryEncode(source, i, out var packet), Is.True);
      Assert.That(packet.Data.Length, Is.EqualTo(width * height * 2));
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv422P8));
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData), "every sample of every plane");
      });
    }
  }

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void DescribesAStreamTheDecoderAccepts(string tag) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(16, 9, tag, 2));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters(tag)));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Width, Is.EqualTo(16));
      Assert.That(described.Height, Is.EqualTo(9));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  /// <summary>A 4:4:4 picture gives up half its chroma columns, and that is the only thing it gives up.</summary>
  [Test]
  [Category("Unit")]
  public void ChromaAlreadySitedAtOnePairPerTwoColumnsSurvivesA444Source() {
    var frame = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Yuv444P8,
      PixelData = [10, 20, 30, 40, 60, 60, 200, 200, 70, 70, 210, 210],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 1, "YUY2"));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (luma, cb, cr) = _DecodePlanes("YUY2", _Stream(4, 1, "YUY2"), packet.Data.ToArray());

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new byte[] { 10, 20, 30, 40 }), "luma is never subsampled");
      Assert.That(cb, Is.EqualTo(new byte[] { 60, 200 }));
      Assert.That(cr, Is.EqualTo(new byte[] { 70, 210 }));
    });
  }

  // ============================================================================================
  // Geometry
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void RefusesAnOddWidthInBothDirectionsAndTakesAnOddHeight(string tag) {
    Assert.Multiple(() => {
      var odd = _Stream(7, 4, tag);
      Assert.That(Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateDecoder(odd))!.Message, Does.Contain("width of 7"));
      Assert.That(Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateEncoder(odd))!.Message, Does.Contain("width of 7"));

      var oddHeight = _Stream(8, 7, tag);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(oddHeight), Throws.Nothing);
      Assert.That(() => VideoFormatRegistry.CreateEncoder(oddHeight), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase("YUY2")]
  [TestCase("YVYU")]
  [TestCase("UYVY")]
  [TestCase("VYUY")]
  public void RefusesAPictureWithNoPixels(string tag) {
    var failure = Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.CreateDecoder(_Stream(0, 4, tag)));

    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "YUY2"));
    Assert.That(encoder.TryEncode(_Random(4, 2, 7), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Random(4, 4, 8), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("4x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithTooLittlePixelData() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "UYVY"));
    var truncated = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Yuv422P8, PixelData = new byte[15] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(truncated, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamOfAnotherKind() {
    Assert.Throws<NotSupportedException>(
      () => Yuy2VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("YUY2"), Width = 4, Height = 4 }));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, "YVYU"));

    Assert.That(encoder.TryEncode(_Random(4, 2, 11), 42, out var packet), Is.True);
    Assert.That(encoder.TryEncode(_Random(4, 2, 12), null, out var untimed), Is.True);

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
