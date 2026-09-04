using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v308 encoder, against the package's own decoder and the packing written out by hand.
/// </summary>
[TestFixture]
public class V308VideoEncoderTests {

  private static readonly CodecTag _V308 = CodecTag.FromCharacters("v308");

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

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = V308VideoEncoder.Create(_Stream(17, 9, 2));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(V308VideoEncoder.Codec, Is.EqualTo(_V308));
      Assert.That(described.Codec, Is.EqualTo(_V308));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Width, Is.EqualTo(17));
      Assert.That(described.Height, Is.EqualTo(9));
      Assert.That(described.BitsPerPixel, Is.EqualTo(24));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(described.ContainerPrivateData.IsEmpty, Is.True);
      Assert.That(V308VideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void PacksVThenYThenUWithNoPadding() {
    // Y=81, U=90, V=240 for the first pixel; Y=235, U=128, V=16 for the second.
    var frame = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Yuv444P8,
      PixelData = [81, 235, 90, 128, 240, 16],
    };
    var encoder = V308VideoEncoder.Create(_Stream(2, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 240, 81, 90, 16, 235, 128 }));
  }

  [Test]
  [Category("Unit")]
  [TestCase(17, 9)]
  [TestCase(4, 2)]
  [TestCase(64, 41)]
  public void RoundTripsPseudoRandomSamplesThroughTheRegistryDecoder(int width, int height) {
    var encoder = V308VideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < 3; ++i) {
      var source = _RandomPlanar(width, height, 1000 + i);
      Assert.That(encoder.TryEncode(source, i, out var packet), Is.True);
      Assert.That(packet.Data.Length, Is.EqualTo(width * height * 3));

      // The samples themselves, byte for byte.
      var (luma, cb, cr) = ((V308VideoDecoder)decoder).DecodePlanes(packet.Data.Span);
      Assert.That(luma, Is.EqualTo(source.GetPlaneData(0).ToArray()), "luma");
      Assert.That(cb, Is.EqualTo(source.GetPlaneData(1).ToArray()), "cb");
      Assert.That(cr, Is.EqualTo(source.GetPlaneData(2).ToArray()), "cr");

      // And the picture the decoder hands out, which is the package's own BT.601 reading of them.
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(FastRawImageConverter.Convert(source, PixelFormat.Rgb24).PixelData));
    }
  }

  [Test]
  [Category("Unit")]
  public void TakesRgb24ThroughTheDecodersOwnMatrix() {
    // White, black and mid grey under BT.601 studio swing: Y 235, 16 and 126 with neutral chroma.
    var frame = new RawImage {
      Width = 3,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 255, 255, 0, 0, 0, 128, 128, 128],
    };
    var encoder = V308VideoEncoder.Create(_Stream(3, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var expected = FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);
    var (luma, cb, cr) = V308VideoDecoder.Create(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new byte[] { 235, 16, 126 }));
      Assert.That(cb, Is.EqualTo(new byte[] { 128, 128, 128 }));
      Assert.That(cr, Is.EqualTo(new byte[] { 128, 128, 128 }));
      Assert.That(luma, Is.EqualTo(expected.GetPlaneData(0).ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreKeyFramesCarryingTheTimestampsGiven() {
    var encoder = V308VideoEncoder.Create(_Stream(4, 2, 3));

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
    var encoder = V308VideoEncoder.Create(_Stream(4, 2));
    Assert.That(encoder.TryEncode(_RandomPlanar(4, 2, 1), 0, out _), Is.True);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_RandomPlanar(4, 3, 2), 1, out _));

    Assert.That(failure!.Message, Does.Contain("4x2").And.Contain("4x3"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPixelFormatItCannotCarryLosslesslyByName() {
    var encoder = V308VideoEncoder.Create(_Stream(2, 1));
    var gray = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Gray8, PixelData = new byte[2] };
    var rgba = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgba32, PixelData = new byte[8] };

    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(gray, 0, out _))!.Message, Does.Contain("Gray8"));
      Assert.That(Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgba, 0, out _))!.Message, Does.Contain("Rgba32"));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithTooLittlePixelData() {
    var encoder = V308VideoEncoder.Create(_Stream(4, 2));
    var short_ = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Yuv444P8, PixelData = new byte[23] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(short_, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamWithNoPixelsOrOfAnotherKind() {
    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => V308VideoEncoder.Create(_Stream(0, 4)))!.Message, Does.Contain("0x4"));
      Assert.Throws<NotSupportedException>(() => V308VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 }));
    });
  }
}
