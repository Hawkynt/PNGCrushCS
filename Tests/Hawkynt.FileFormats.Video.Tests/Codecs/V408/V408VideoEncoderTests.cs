using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v408 encoder, against the package's own decoder and the packing written out by hand.
/// </summary>
[TestFixture]
public class V408VideoEncoderTests {

  private static readonly CodecTag _V408 = CodecTag.FromCharacters("v408");

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _RandomPlanar(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = pixels };
  }

  private static RawImage _RandomRgba(int width, int height, int seed) {
    var pixels = new byte[width * height * 4];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = V408VideoEncoder.Create(_Stream(17, 9, 2));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(V408VideoEncoder.Codec, Is.EqualTo(_V408));
      Assert.That(described.Codec, Is.EqualTo(_V408));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Width, Is.EqualTo(17));
      Assert.That(described.Height, Is.EqualTo(9));
      Assert.That(described.BitsPerPixel, Is.EqualTo(32));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(described.ContainerPrivateData.IsEmpty, Is.True);
      Assert.That(V408VideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void PacksUThenYThenVThenOpaqueAlphaFromPlanarInput() {
    // Y=81, U=90, V=240 for the first pixel; Y=235, U=128, V=16 for the second.
    var frame = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Yuv444P8,
      PixelData = [81, 235, 90, 128, 240, 16],
    };
    var encoder = V408VideoEncoder.Create(_Stream(2, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 90, 81, 240, 255, 128, 235, 16, 255 }));
  }

  [Test]
  [Category("Unit")]
  [TestCase(17, 9)]
  [TestCase(4, 2)]
  [TestCase(64, 41)]
  public void RoundTripsPseudoRandomSamplesThroughTheRegistryDecoder(int width, int height) {
    var encoder = V408VideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < 3; ++i) {
      var source = _RandomPlanar(width, height, 2000 + i);
      Assert.That(encoder.TryEncode(source, i, out var packet), Is.True);
      Assert.That(packet.Data.Length, Is.EqualTo(width * height * 4));

      var (luma, cb, cr, alpha) = ((V408VideoDecoder)decoder).DecodePlanes(packet.Data.Span);
      Assert.That(luma, Is.EqualTo(source.GetPlaneData(0).ToArray()), "luma");
      Assert.That(cb, Is.EqualTo(source.GetPlaneData(1).ToArray()), "cb");
      Assert.That(cr, Is.EqualTo(source.GetPlaneData(2).ToArray()), "cr");
      Assert.That(alpha, Is.All.EqualTo(255), "alpha");

      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      var expected = FastRawImageConverter.Convert(source, PixelFormat.Rgba32).PixelData;
      Assert.That(decoded.PixelData, Is.EqualTo(expected));
    }
  }

  [Test]
  [Category("Unit")]
  [TestCase(17, 9)]
  [TestCase(3, 5)]
  public void CarriesAlphaFromRgba32ThroughUnchangedAndColourThroughTheDecodersOwnMatrix(int width, int height) {
    var encoder = V408VideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    var source = _RandomRgba(width, height, 77);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var expected = FastRawImageConverter.Convert(source, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);
    var (luma, cb, cr, alpha) = ((V408VideoDecoder)decoder).DecodePlanes(packet.Data.Span);
    var expectedAlpha = new byte[width * height];
    for (var i = 0; i < expectedAlpha.Length; ++i)
      expectedAlpha[i] = source.PixelData[i * 4 + 3];

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(expected.GetPlaneData(0).ToArray()));
      Assert.That(cb, Is.EqualTo(expected.GetPlaneData(1).ToArray()));
      Assert.That(cr, Is.EqualTo(expected.GetPlaneData(2).ToArray()));
      Assert.That(alpha, Is.EqualTo(expectedAlpha));
    });

    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    for (var i = 0; i < expectedAlpha.Length; ++i)
      Assert.That(decoded.PixelData[i * 4 + 3], Is.EqualTo(expectedAlpha[i]), $"alpha of pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void TakesRgb24WithOpaqueAlpha() {
    var frame = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 255, 255, 0, 0, 0] };
    var encoder = V408VideoEncoder.Create(_Stream(2, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 128, 235, 128, 255, 128, 16, 128, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = V408VideoEncoder.Create(_Stream(4, 2, 3));

    Assert.That(encoder.TryEncode(_RandomPlanar(4, 2, 1), 42, out var packet), Is.True);
    Assert.That(encoder.TryEncode(_RandomPlanar(4, 2, 2), null, out var untimed), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(untimed.PresentationTimestamp, Is.Null);
      Assert.That(untimed.DecodeTimestamp, Is.Null);
      Assert.That(untimed.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = V408VideoEncoder.Create(_Stream(4, 2));
    Assert.That(encoder.TryEncode(_RandomPlanar(4, 2, 1), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_RandomRgba(5, 2, 2), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("5x2"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPixelFormatItCannotCarryLosslesslyByName() {
    var encoder = V408VideoEncoder.Create(_Stream(2, 1));
    var gray = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Gray8, PixelData = new byte[2] };
    var subsampled = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Yuv422P8, PixelData = new byte[4] };

    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(gray, 0, out _))!.Message, Does.Contain("Gray8"));
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(subsampled, 0, out _))!.Message, Does.Contain("Yuv422P8"));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithTooLittlePixelData() {
    var encoder = V408VideoEncoder.Create(_Stream(4, 2));
    var short_ = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Rgba32, PixelData = new byte[31] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(short_, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamWithNoPixelsOrOfAnotherKind() {
    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => V408VideoEncoder.Create(_Stream(4, 0)))!.Message, Does.Contain("4x0"));
      Assert.Throws<NotSupportedException>(() => V408VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 }));
    });
  }
}
