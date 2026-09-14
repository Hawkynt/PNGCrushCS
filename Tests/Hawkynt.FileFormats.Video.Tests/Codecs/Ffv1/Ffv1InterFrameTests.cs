using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>FFV1's only dependency between pictures: carrying entropy-coder statistics.</summary>
[TestFixture]
public class Ffv1InterFrameTests {

  [Test]
  [Category("Unit")]
  public void ALongerKeyframeIntervalCarriesEntropyStateAndStillCodesEveryPicture() {
    var encoder = Ffv1Encoder.Create(_Stream(19, 13), PixelFormat.Rgb24, 1, 1, keyFrameInterval: 3);
    var pictures = new[] { _Picture(19, 13, 1), _Picture(19, 13, 2), _Picture(19, 13, 3), _Picture(19, 13, 4), _Picture(19, 13, 5) };
    var packets = new CodedPacket[pictures.Length];

    for (var i = 0; i < pictures.Length; ++i)
      Assert.That(encoder.TryEncode(pictures[i], i * 40, out packets[i]), Is.True);

    Assert.That(packets, Has.Length.EqualTo(5));
    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
      Assert.That(packets[2].IsKeyFrame, Is.False);
      Assert.That(packets[3].IsKeyFrame, Is.True);
      Assert.That(packets[4].IsKeyFrame, Is.False);
    });

    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    for (var i = 0; i < packets.Length; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ConfigurationRecordSaysWhetherEveryFrameMustBeAKeyframe() {
    var everyFrame = _Parameters(Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray8, 1, 1).DescribeStream());
    var gop = _Parameters(Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray8, 1, 1, keyFrameInterval: 12).DescribeStream());

    Assert.Multiple(() => {
      Assert.That(everyFrame.IntraOnly, Is.True);
      Assert.That(gop.IntraOnly, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void ADecoderCannotEnterAStateDependentStreamAtANonKeyframe() {
    var encoder = Ffv1Encoder.Create(_Stream(12, 9), PixelFormat.Rgb24, 1, 1, keyFrameInterval: 4);
    encoder.TryEncode(_Picture(12, 9, 7), 0, out _);
    encoder.TryEncode(_Picture(12, 9, 8), 1, out var dependent);
    Assert.That(dependent.IsKeyFrame, Is.False);

    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(dependent, out _));
    Assert.That(failure!.Message, Does.Contain("keyframe").And.Contain("state"));
  }

  [Test]
  [Category("Unit")]
  public void KeyframeIntervalMustBePositive() {
    var stream = _Stream(8, 8);

    Assert.Throws<ArgumentOutOfRangeException>(() => Ffv1Encoder.Create(stream, PixelFormat.Gray8, 1, 1, keyFrameInterval: 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => Ffv1Encoder.Create(stream, PixelFormat.Gray8, 1, 1, keyFrameInterval: -1));
  }

  private static Ffv1Parameters _Parameters(MediaStreamInfo stream) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return Ffv1Parameters.Read(new Ffv1RangeCoder(stream.CodecPrivateData[..^4], zero, one), states, true);
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 4,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    BitsPerPixel = 24,
  };

  private static RawImage _Picture(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    var random = new Random(seed);
    random.NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
