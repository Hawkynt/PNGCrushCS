using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Y800 decoder and encoder: one byte a pixel, no chroma, and no geometry it will not take.
/// </summary>
/// <remarks>
/// Measured against ffmpeg over pseudo-random content at 16x8, 17x9 and 7x5, five frames apiece, in
/// both directions: 1,580 samples, none differing. What these tests hold in place is the part a
/// sample comparison cannot state — that the bytes are handed over unscaled, and that a size the
/// other nine layouts refuse is written and read here.
/// </remarks>
[TestFixture]
public class Y800CodecTests {

  private static readonly CodecTag _Y800 = CodecTag.FromCharacters("Y800");

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = _Y800,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Random(int width, int height, int seed) {
    var pixels = new byte[width * height];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void AcceptsTheY800TagIgnoringCaseAndNothingElse() {
    Assert.Multiple(() => {
      Assert.That(Y800VideoDecoder.Accepts(_Stream(4, 2)), Is.True);
      Assert.That(Y800VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("y800"), Width = 4, Height = 2 }), Is.True);
      Assert.That(Y800VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("Y41P"), Width = 4, Height = 2 }), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void HandsTheLumaBytesBackUnchangedAndUnscaled() {
    // The ends of the studio-swing range and one value outside it: nothing here is rescaled.
    var packet = new byte[] { 16, 235, 0, 255, 128, 77 };
    var decoder = Y800VideoDecoder.Create(_Stream(3, 2));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(frame.Width, Is.EqualTo(3));
      Assert.That(frame.Height, Is.EqualTo(2));
      Assert.That(frame.PixelData, Is.EqualTo(packet));
    });
  }

  [Test]
  [Category("Unit")]
  public void ARowIsExactlyWidthBytesWithNoPaddingAtAll() {
    var decoder = Y800VideoDecoder.Create(_Stream(7, 5));

    Assert.That(() => decoder.DecodeLuma(new byte[35]), Throws.Nothing);
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodeLuma(new byte[34]));
    Assert.That(failure!.Message, Does.Contain("34 byte(s)").And.Contain("needs 35"));
  }

  /// <summary>The one of the ten layouts with no chroma grid, and so the one with no even-size rule.</summary>
  [Test]
  [Category("Unit")]
  [TestCase(7, 8)]
  [TestCase(8, 7)]
  [TestCase(7, 5)]
  [TestCase(1, 1)]
  public void TakesAnyPictureSizeAtAllInBothDirections(int width, int height) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    var source = _Random(width, height, 4000 + width * 100 + height);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(width * height));
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void TakesThePlanarSourcesLumaPlaneAsItIs() {
    var frame = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = [10, 20, 30, 40, 90, 200],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(2, 2));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void TakesRgb24ThroughTheStudioSwingMatrix() {
    // White, black and mid grey under BT.601 studio swing: Y 235, 16 and 126.
    var frame = new RawImage {
      Width = 3,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 255, 255, 0, 0, 0, 128, 128, 128],
    };
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(3, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 235, 16, 126 }));
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(17, 9, 4));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(Y800VideoEncoder.Codec, Is.EqualTo(_Y800));
      Assert.That(described.Codec, Is.EqualTo(_Y800));
      Assert.That(described.Index, Is.EqualTo(4));
      Assert.That(described.Width, Is.EqualTo(17));
      Assert.That(described.Height, Is.EqualTo(9));
      Assert.That(described.BitsPerPixel, Is.EqualTo(8));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(Y800VideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixelsAndAStreamOfAnotherKind() {
    Assert.Multiple(() => {
      Assert.That(Assert.Throws<InvalidDataException>(() => Y800VideoDecoder.Create(_Stream(0, 4)))!.Message, Does.Contain("0x4"));
      Assert.That(Assert.Throws<InvalidDataException>(() => Y800VideoEncoder.Create(_Stream(4, 0)))!.Message, Does.Contain("4x0"));
      Assert.Throws<NotSupportedException>(
        () => Y800VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = _Y800, Width = 4, Height = 4 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2));
    Assert.That(encoder.TryEncode(_Random(4, 2, 15), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Random(4, 3, 16), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("4x3"));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(4, 2, 1));

    Assert.That(encoder.TryEncode(_Random(4, 2, 17), 42, out var packet), Is.True);
    Assert.That(encoder.TryEncode(_Random(4, 2, 18), null, out var untimed), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(untimed.PresentationTimestamp, Is.Null);
      Assert.That(untimed.IsKeyFrame, Is.True);
      Assert.That(encoder.Flush(), Is.Empty);
    });
  }
}
