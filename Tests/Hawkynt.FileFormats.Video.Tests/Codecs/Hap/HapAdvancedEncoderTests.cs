using System;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>Coverage for the Hap texture variants beyond the legacy DXT1/DXT5/Hap Q writer.</summary>
[TestFixture]
public sealed class HapAdvancedEncoderTests {

  private static MediaStreamInfo _Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _RoundTrip(string code, RawImage source, out byte[] encoded) {
    var encoder = HapVideoEncoder.Create(_Stream(code, source.Width, source.Height));
    Assert.That(encoder.TryEncode(source, 17, out var packet), Is.True);
    encoded = packet.Data.ToArray();

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    return decoded;
  }

  [Test]
  [Category("Unit")]
  public void HapAlphaOnlyPreservesEveryConstantBlockValue() {
    const int width = 256;
    const int height = 16;
    var pixels = new byte[width * height];
    var blocksAcross = width / 4;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = (byte)(x / 4 + y / 4 * blocksAcross);

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
    var decoded = _RoundTrip("HapA", source, out var encoded);

    Assert.Multiple(() => {
      Assert.That(encoded[3] & 0x0F, Is.EqualTo(0x01));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void HapQAlphaWritesTheSpecifiedTwoImageCombinationAndPreservesAlpha() {
    const int width = 32;
    const int height = 16;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        var block = x / 4 + y / 4 * (width / 4);
        pixels[at] = (byte)(20 + x * 3);
        pixels[at + 1] = (byte)(30 + y * 5);
        pixels[at + 2] = (byte)(200 - x * 2);
        pixels[at + 3] = (byte)(block * 7);
      }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var decoded = _RoundTrip("HapM", source, out var encoded);
    var textures = HapFrameParser.ParseFrame(encoded);

    Assert.Multiple(() => {
      Assert.That(encoded[3], Is.EqualTo(0x0D));
      Assert.That(textures, Has.Count.EqualTo(2));
      Assert.That(textures[0].Format, Is.EqualTo(HapPixelFormat.Dxt5ScaledYCoCg));
      Assert.That(textures[1].Format, Is.EqualTo(HapPixelFormat.Rgtc1Alpha));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    });

    for (var pixel = 0; pixel < width * height; ++pixel)
      Assert.That(decoded.PixelData[pixel * 4 + 3], Is.EqualTo(pixels[pixel * 4 + 3]), $"alpha at pixel {pixel}");
  }

  [Test]
  [Category("Unit")]
  public void HapRMode6KeepsConstantEndpointsWhenTheirChannelParityAgrees() {
    const int width = 8;
    const int height = 4;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var colour = x < 4
          ? (R: (byte)10, G: (byte)20, B: (byte)30, A: (byte)40)
          : (R: (byte)11, G: (byte)21, B: (byte)31, A: (byte)41);
        var at = (y * width + x) * 4;
        pixels[at] = colour.R;
        pixels[at + 1] = colour.G;
        pixels[at + 2] = colour.B;
        pixels[at + 3] = colour.A;
      }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var decoded = _RoundTrip("Hap7", source, out var encoded);

    Assert.Multiple(() => {
      Assert.That(encoded[3] & 0x0F, Is.EqualTo(0x0C));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [TestCase(false, 0x02)]
  [TestCase(true, 0x03)]
  [Category("Unit")]
  public void HapHdrChoosesUnsignedOrSignedBc6PerFrame(bool negative, int expectedFormat) {
    const int width = 5;
    const int height = 3;
    var pixels = _RgbF16(width, height, (x, y) => (
      negative ? (Half)(-1.0f - x * 0.05f) : (Half)(1.0f + x * 0.05f),
      (Half)(0.5f + y * 0.1f),
      (Half)2.0f));
    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.RgbF16, PixelData = pixels };

    var decoded = _RoundTrip("HapH", source, out var encoded);

    Assert.Multiple(() => {
      Assert.That(encoded[3] & 0x0F, Is.EqualTo(expectedFormat));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.RgbF16));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
    });

    for (var pixel = 0; pixel < width * height; ++pixel)
      for (var channel = 0; channel < 3; ++channel) {
        var expected = _ReadHalf(pixels, (pixel * 3 + channel) * 2);
        var actual = _ReadHalf(decoded.PixelData, (pixel * 3 + channel) * 2);
        Assert.That(MathF.Abs(actual - expected), Is.LessThan(0.1f), $"pixel {pixel}, channel {channel}");
      }
  }

  [Test]
  [Category("Unit")]
  public void HapHdrRefusesNonFiniteSamplesInsteadOfInventingAValue() {
    var pixels = _RgbF16(4, 4, (_, _) => ((Half)1, (Half)2, (Half)3));
    pixels[0] = 0x00;
    pixels[1] = 0x7C; // +infinity
    var source = new RawImage { Width = 4, Height = 4, Format = PixelFormat.RgbF16, PixelData = pixels };
    var encoder = HapVideoEncoder.Create(_Stream("HapH", 4, 4));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(source, 0, out _));
    Assert.That(failure!.Message, Does.Contain("infinity or NaN"));
  }

  [Test]
  [Category("Unit")]
  public void HapROddDimensionsUseOneCompleteEdgeBlockAndCropItAgain() {
    const int width = 7;
    const int height = 5;
    var pixels = new byte[width * height * 4];
    for (var pixel = 0; pixel < width * height; ++pixel) {
      pixels[pixel * 4] = 10;
      pixels[pixel * 4 + 1] = 20;
      pixels[pixel * 4 + 2] = 30;
      pixels[pixel * 4 + 3] = 40;
    }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var decoded = _RoundTrip("Hap7", source, out _);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  private static byte[] _RgbF16(int width, int height, Func<int, int, (Half R, Half G, Half B)> pixel) {
    var result = new byte[width * height * 6];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (r, g, b) = pixel(x, y);
        var at = (y * width + x) * 6;
        _WriteHalf(result, at, r);
        _WriteHalf(result, at + 2, g);
        _WriteHalf(result, at + 4, b);
      }

    return result;
  }

  private static void _WriteHalf(Span<byte> destination, int offset, Half value) {
    var bits = BitConverter.HalfToUInt16Bits(value);
    destination[offset] = (byte)bits;
    destination[offset + 1] = (byte)(bits >> 8);
  }

  private static float _ReadHalf(ReadOnlySpan<byte> source, int offset) {
    var bits = (ushort)(source[offset] | (source[offset + 1] << 8));
    return (float)BitConverter.UInt16BitsToHalf(bits);
  }
}
