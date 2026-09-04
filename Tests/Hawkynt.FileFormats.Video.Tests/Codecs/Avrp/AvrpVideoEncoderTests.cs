using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The avrp encoder, against the package's own decoder and the word written out by hand.
/// </summary>
/// <remarks>
/// The encoder as a whole was measured against ffmpeg's own <c>avrp</c> encoder — one of the few
/// codecs here whose reference has one — over the same <c>gbrp10le</c> planes at 8x2, 33x25, 64x40
/// and 100x30: the two packets are identical on every byte, padding columns included. What these
/// tests add is which bit range of the word a mistake would have hidden in, and that the padding
/// columns are written as nothing rather than left holding the row before them.
/// </remarks>
[TestFixture]
public class AvrpVideoEncoderTests {

  private static readonly CodecTag _AVRP = CodecTag.FromCharacters("AVrp");

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1001, 30000),
    FrameRate = new Rational(30000, 1001),
  };

  private static MediaStreamInfo _Audio(int width, int height) => new() {
    Index = 0, Kind = MediaStreamKind.Audio, Width = width, Height = height,
  };

  /// <summary>Pseudo-random ten-bit samples with the alpha field fully opaque, which is what the decoder hands back.</summary>
  private static RawImage _RandomRgb30(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      var word = (uint)random.Next(1 << 30) | 0xC0000000u;
      BitConverter.TryWriteBytes(pixels.AsSpan(i * 4), word);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb30, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = AvrpVideoEncoder.Create(_Stream(33, 25, 1));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(AvrpVideoEncoder.Codec, Is.EqualTo(_AVRP));
      Assert.That(described.Codec, Is.EqualTo(_AVRP));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(1));
      Assert.That(described.Width, Is.EqualTo(33));
      Assert.That(described.Height, Is.EqualTo(25));
      Assert.That(described.BitsPerPixel, Is.EqualTo(32));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1001, 30000)));
      Assert.That(AvrpVideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void PacksRedHighGreenMiddleBlueLowLittleEndianWithTheLowTwoBitsZero() {
    // R=500, G=300, B=700 in Rgb30's own layout is R | G<<10 | B<<20; avrp's word is
    // R<<22 | G<<12 | B<<2, stored little-endian, with the low two bits spare.
    var pixels = new byte[4];
    BitConverter.TryWriteBytes(pixels, 500u | (300u << 10) | (700u << 20) | 0xC0000000u);
    var frame = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb30, PixelData = pixels };
    var encoder = AvrpVideoEncoder.Create(_Stream(1, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    var word = BitConverter.ToUInt32(data, 0);
    Assert.Multiple(() => {
      // One pixel still pads out to a whole sixty-four-pixel block.
      Assert.That(data, Has.Length.EqualTo(64 * 4));
      Assert.That((word >> 22) & 0x3FF, Is.EqualTo(500u), "red");
      Assert.That((word >> 12) & 0x3FF, Is.EqualTo(300u), "green");
      Assert.That((word >> 2) & 0x3FF, Is.EqualTo(700u), "blue");
      Assert.That(word & 3, Is.Zero, "the low two bits are spare");
      Assert.That(packet.IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void PadsEveryRowToAWholeBlockAndLeavesThePaddingAtZero() {
    var frame = _RandomRgb30(100, 3, 7);
    var encoder = AvrpVideoEncoder.Create(_Stream(100, 3));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    const int _STRIDE = 128 * 4;
    Assert.That(data, Has.Length.EqualTo(_STRIDE * 3));
    for (var y = 0; y < 3; ++y)
      for (var at = y * _STRIDE + 100 * 4; at < (y + 1) * _STRIDE; ++at)
        Assert.That(data[at], Is.Zero, $"padding byte {at}");
  }

  [Test]
  [Category("Unit")]
  public void RoundTripsThroughTheDecoderSampleForSample() {
    foreach (var (width, height) in new[] { (8, 2), (33, 25), (64, 40), (100, 30), (1, 1) }) {
      var frame = _RandomRgb30(width, height, width * 31 + height);
      var encoder = AvrpVideoEncoder.Create(_Stream(width, height));
      Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

      var decoder = AvrpVideoDecoder.Create(encoder.DescribeStream());
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb30), $"{width}x{height}");
        Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData).AsCollection, $"{width}x{height}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void TakesAnEightBitPictureWidenedToTenBitsWithoutLosingASample() {
    var pixels = new byte[6 * 4 * 3];
    new Random(11).NextBytes(pixels);
    var frame = new RawImage { Width = 6, Height = 4, Format = PixelFormat.Rgb24, PixelData = pixels };
    var encoder = AvrpVideoEncoder.Create(_Stream(6, 4));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var decoder = AvrpVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    var back = FastRawImageConverter.Convert(decoded, PixelFormat.Rgb24);
    Assert.That(back.PixelData, Is.EqualTo(pixels).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void RefusesWhatItCannotWrite() {
    var encoder = AvrpVideoEncoder.Create(_Stream(8, 4));
    var wrongSize = _RandomRgb30(9, 4, 3);
    var wrongFormat = new RawImage {
      Width = 8, Height = 4, Format = PixelFormat.Rgba32, PixelData = new byte[8 * 4 * 4],
    };

    Assert.Multiple(() => {
      Assert.That(() => AvrpVideoEncoder.Create(_Audio(8, 4)), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => AvrpVideoEncoder.Create(_Stream(0, 4)), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(wrongSize, 0, out _), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(wrongFormat, 0, out _), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => encoder.TryEncode(null!, 0, out _), Throws.TypeOf<ArgumentNullException>());
    });
  }
}
