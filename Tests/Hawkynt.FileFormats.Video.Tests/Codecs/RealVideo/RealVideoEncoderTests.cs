using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.RealVideo.Tests;

[TestFixture]
public sealed class RealVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void AFlatPictureRoundTripsExactly() {
    var stream = _Stream(176, 144);
    var encoder = RealVideoEncoder.Create(stream);
    var decoder = RealVideoDecoder.Create(encoder.DescribeStream());
    var source = _Flat(176, 144, 99, 128);

    Assert.That(encoder.TryEncode(source, 7, out var packet), Is.True);
    var planes = decoder.DecodePlanes(packet);

    Assert.Multiple(() => {
      Assert.That(planes.Luma.Distinct().ToArray(), Is.EqualTo(new byte[] { 99 }));
      Assert.That(planes.Cb.Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(planes.Cr.Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.FragmentOffsets, Is.EqualTo(new[] { 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ATexturedPictureUsesTheCoefficientLayerAndComesBackClose() {
    const int width = 176;
    const int height = 144;
    var source = _Textured(width, height);
    var encoder = RealVideoEncoder.Create(_Stream(width, height));
    var decoder = RealVideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoded = decoder.DecodePlanes(packet);
    var error = _MeanSquaredError(source.PixelData.AsSpan(0, width * height), decoded.Luma);

    Assert.That(error, Is.LessThan(40d));
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesRevisionZeroRv10() {
    var stream = RealVideoEncoder.Create(_Stream(352, 288)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("RV10")));
      Assert.That(stream.CodecId, Is.EqualTo("V_REAL/RV10"));
      Assert.That(stream.Width, Is.EqualTo(352));
      Assert.That(stream.Height, Is.EqualTo(288));
      Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 8, 0x10, 0, 0, 0 }));
      Assert.That(RealVideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesTheWriter() {
    var stream = _Stream(176, 144);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<RealVideoEncoder>());
    });
  }

  [TestCase(175, 144)]
  [TestCase(176, 143)]
  [TestCase(0, 0)]
  [TestCase(1024, 1024)]
  [Category("Unit")]
  public void GeometryTheSingleRunSyntaxCannotWriteIsRefused(int width, int height)
    => Assert.Throws<NotSupportedException>(() => RealVideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AFrameWhoseSizeChangesIsRefused() {
    var encoder = RealVideoEncoder.Create(_Stream(176, 144));
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(352, 288, 99, 128), 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack()
    => Assert.That(RealVideoEncoder.Create(_Stream(176, 144)).Flush(), Is.Empty);

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RV10"),
    Width = width,
    Height = height,
    TimeBase = new(1, 1000),
  };

  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(chrominance);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static RawImage _Textured(int width, int height) {
    var luma = width * height;
    var chroma = luma / 4;
    var planes = new byte[luma + 2 * chroma];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        planes[y * width + x] = (byte)(32 + ((x * 5 + y * 3 + (x ^ y)) % 192));

    planes.AsSpan(luma, chroma).Fill(104);
    planes.AsSpan(luma + chroma, chroma).Fill(152);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    double sum = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      sum += difference * difference;
    }

    return sum / expected.Length;
  }
}