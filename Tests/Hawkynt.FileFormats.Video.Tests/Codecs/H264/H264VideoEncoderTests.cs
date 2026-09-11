using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.H264Video;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void EncoderWritesLengthPrefixedIdrThatOwnDecoderReadsExactly() {
    const int width = 18;
    const int height = 20;
    var frame = _Random420(width, height, 0x264);
    var encoder = H264VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Data.Span[..4].ToArray(), Is.Not.EqualTo(new byte[] { 0, 0, 0, 1 }),
        "the codec emits AVC length-prefixed samples rather than Annex B");
    });

    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawWriterTurnsEncoderOutputIntoAnnexBWithParameterSets() {
    var frame = _Random420(32, 16, 42);
    var encoder = H264VideoEncoder.Create(_Stream(frame.Width, frame.Height));
    encoder.TryEncode(frame, 0, out var packet);

    var annexB = VideoIO.Mux<H264VideoWriter>([encoder.DescribeStream()], [packet]);
    var units = H264NalReader.SplitAnnexB(annexB).ToArray();

    Assert.That(units.Select(unit => unit.Type), Is.EqualTo(new[] {
      H264NalUnitType.SequenceParameterSet,
      H264NalUnitType.PictureParameterSet,
      H264NalUnitType.IdrSlice,
    }));

    var decoder = H264VideoDecoder.Create(_Stream(frame.Width, frame.Height));
    Assert.That(decoder.TryDecode(new(0, annexB), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [TestCase(17, 16)]
  [TestCase(16, 17)]
  [Category("Unit")]
  public void Odd420DimensionsAreRefused(int width, int height)
    => Assert.That(
      () => H264VideoEncoder.Create(_Stream(width, height)),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("Both dimensions must be even"));

  [Test]
  [Category("Unit")]
  public void RegistryExposesH264Encoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(16, 16));
    Assert.That(encoder, Is.TypeOf<H264VideoEncoder>());
  }

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      Width = width,
      Height = height,
      BitsPerPixel = 12,
    };

  private static RawImage _Random420(int width, int height, int seed) {
    var pixels = new byte[width * height * 3 / 2];
    new Random(seed).NextBytes(pixels);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }
}
