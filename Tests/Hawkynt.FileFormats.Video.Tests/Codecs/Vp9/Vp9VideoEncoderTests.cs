using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp9.Tests;

[TestFixture]
public class Vp9VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void TheStreamIsDescribedAsLosslessProfileZeroVp9() {
    var encoder = Vp9VideoEncoder.Create(_Stream(64, 48));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("VP90")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("VP90")));
      Assert.That(stream.CodecId, Is.EqualTo("V_VP9"));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(64));
      Assert.That(stream.Height, Is.EqualTo(48));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(12));
      Assert.That(Vp9VideoDecoder.Accepts(stream), Is.True);
    });
  }

  private static IEnumerable<TestCaseData> _RoundTrips() {
    foreach (var (width, height) in new[] {
               (1, 1), (2, 3), (7, 5), (8, 8), (9, 9), (17, 7), (33, 17), (64, 48), (65, 33),
             })
      foreach (var pattern in new[] { 0, 1, 2 })
        yield return new TestCaseData(width, height, pattern)
          .SetName($"RoundTrip({width}x{height},{pattern switch { 0 => "flat", 1 => "gradient", _ => "noise" }})");
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_RoundTrips))]
  public void EveryNativeSampleComesBackExactly(int width, int height, int pattern) {
    var source = _Picture(width, height, pattern);
    var encoder = Vp9VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(source, 123, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(123));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(123));
    });

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ExtremeAlternationExercisesLargeCoefficientCategoriesLosslessly() {
    const int width = 31;
    const int height = 19;
    var source = _Alternating(width, height);
    var encoder = Vp9VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoder = Vp9VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ColourRangeAndMatrixAreCarriedByTheKeyframeHeader() {
    var source = _WithColor(_Picture(11, 9, 1), new RawImageColorInfo {
      Range = RawColorRange.Full,
      Matrix = RawMatrixCoefficients.Bt709,
    });
    var encoder = Vp9VideoEncoder.Create(_Stream(11, 9));

    encoder.TryEncode(source, null, out var packet);
    var decoder = Vp9VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(decoded.ColorInfo, Is.Not.Null);
      Assert.That(decoded.ColorInfo!.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(decoded.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt709));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheSamePictureAlwaysProducesTheSameIndependentKeyframe() {
    var source = _Picture(23, 15, 2);
    var encoder = Vp9VideoEncoder.Create(_Stream(23, 15));

    encoder.TryEncode(source, 1, out var first);
    encoder.TryEncode(source, 2, out var second);

    Assert.Multiple(() => {
      Assert.That(second.Data.ToArray(), Is.EqualTo(first.Data.ToArray()));
      Assert.That(second.IsKeyFrame, Is.True);
      Assert.That(second.PresentationTimestamp, Is.EqualTo(2));
    });

    var freshDecoder = Vp9VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(freshDecoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void NonNativePicturesAreRefusedRatherThanSilentlyConverted() {
    var encoder = Vp9VideoEncoder.Create(_Stream(4, 4));
    var rgb = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3],
    };

    Assert.That(
      () => encoder.TryEncode(rgb, 0, out _),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains(nameof(PixelFormat.Yuv420P8)));
  }

  [Test]
  [Category("Unit")]
  public void AShortPlaneBufferAndWrongGeometryAreRefused() {
    var encoder = Vp9VideoEncoder.Create(_Stream(8, 8));
    var shortPicture = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Yuv420P8,
      PixelData = new byte[10],
    };
    var wrongSize = _Picture(7, 8, 0);

    Assert.Multiple(() => {
      Assert.That(() => encoder.TryEncode(shortPicture, 0, out _), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(wrongSize, 0, out _), Throws.TypeOf<InvalidDataException>());
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLosslessForwardTransformIsTheExactInverseOfReconstruction() {
    var random = new Random(0x5A17);
    var inverse = new Vp9InverseTransform();
    var residual = new int[16];
    var coefficients = new int[16];

    for (var sample = 0; sample < 1000; ++sample) {
      for (var i = 0; i < residual.Length; ++i)
        residual[i] = random.Next(-255, 256);

      Vp9ForwardTransform.WalshHadamard4x4(residual, coefficients);
      for (var i = 0; i < coefficients.Length; ++i)
        coefficients[i] *= 4; // lossless dequantiser at qindex zero

      inverse.Apply(coefficients, 2, Vp9Constants.DCT_DCT, true);
      Assert.That(coefficients.ToArray(), Is.EqualTo(residual.ToArray()), $"sample {sample}");
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 3,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 1000),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Picture(int width, int height, int pattern) {
    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;
    var data = new byte[width * height + 2 * chromaWidth * chromaHeight];
    var random = new Random(width * 397 ^ height * 31 ^ pattern * 7919);

    for (var i = 0; i < data.Length; ++i)
      data[i] = pattern switch {
        0 => (byte)(i < width * height ? 73 : 128),
        1 => (byte)((i * 37 + i / Math.Max(1, width) * 19) & 0xFF),
        _ => (byte)random.Next(256),
      };

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = data,
    };
  }

  private static RawImage _Alternating(int width, int height) {
    var image = _Picture(width, height, 0);
    for (var i = 0; i < image.PixelData.Length; ++i)
      image.PixelData[i] = (i & 1) == 0 ? (byte)0 : byte.MaxValue;
    return image;
  }

  private static RawImage _WithColor(RawImage source, RawImageColorInfo colorInfo) => new() {
    Width = source.Width,
    Height = source.Height,
    Format = source.Format,
    PixelData = source.PixelData,
    ColorInfo = colorInfo,
  };
}
