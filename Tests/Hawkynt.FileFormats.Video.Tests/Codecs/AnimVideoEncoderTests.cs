using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class AnimVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void TheAnimEncoderIsRegistered() {
    var stream = _Stream(16, 1);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<AnimVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void FirstIndexedFrameRoundTripsExactly() {
    var stream = _Stream(16, 2);
    var encoder = AnimVideoEncoder.Create(stream);
    var source = _Frame(16, 2, [0, 1, 2, 3], Enumerable.Range(0, 32).Select(i => (byte)(i & 3)).ToArray());

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);

    var decoder = AnimVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    Assert.That(decoded.Palette, Is.EqualTo(source.Palette));
  }

  [Test]
  [Category("Unit")]
  public void SparseFramesUseMethodFiveAndRoundTripAgainstTwoFramesBack() {
    const int width = 320;
    const int height = 8;
    var encoder = AnimVideoEncoder.Create(_Stream(width, height));
    var decoder = AnimVideoDecoder.Create(encoder.DescribeStream());

    var firstPixels = new byte[width * height];
    var secondPixels = new byte[width * height];
    secondPixels[0] = 1;
    var thirdPixels = new byte[width * height];
    thirdPixels[1] = 1;

    encoder.TryEncode(_Frame(width, height, [0, 1], firstPixels), 0, out var first);
    encoder.TryEncode(_Frame(width, height, [0, 1], secondPixels), 1, out var second);
    encoder.TryEncode(_Frame(width, height, [0, 1], thirdPixels), 2, out var third);

    Assert.That(_HasChunk(second.Data.Span, "DLTA"), Is.True);
    Assert.That(_HasChunk(third.Data.Span, "DLTA"), Is.True);

    decoder.TryDecode(first, out var decoded1);
    decoder.TryDecode(second, out var decoded2);
    decoder.TryDecode(third, out var decoded3);
    Assert.That(decoded1.PixelData, Is.EqualTo(firstPixels));
    Assert.That(decoded2.PixelData, Is.EqualTo(secondPixels));
    Assert.That(decoded3.PixelData, Is.EqualTo(thirdPixels));
  }

  [Test]
  [Category("Unit")]
  public void PaletteChangesRideOnPredictedFrames() {
    const int width = 320;
    const int height = 4;
    var encoder = AnimVideoEncoder.Create(_Stream(width, height));
    var decoder = AnimVideoDecoder.Create(encoder.DescribeStream());
    var pixels = new byte[width * height];
    pixels[0] = 1;

    encoder.TryEncode(_Frame(width, height, [0, 1], pixels), 0, out var first);
    encoder.TryEncode(_Frame(width, height, [0, 2], pixels), 1, out var second);

    decoder.TryDecode(first, out _);
    decoder.TryDecode(second, out var decoded);
    Assert.That(decoded.Palette, Is.EqualTo(_Palette([0, 2])));
  }

  [Test]
  [Category("Unit")]
  public void SourceTimestampsAreConvertedToAnimJiffies() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("ANIM"),
      Width = 16,
      Height = 1,
      TimeBase = new Rational(1, 1000),
    };
    var encoder = AnimVideoEncoder.Create(stream);
    var frame = _Frame(16, 1, [0, 1], new byte[16]);

    encoder.TryEncode(frame, 0, out _);
    encoder.TryEncode(frame, 500, out var second);

    Assert.That(encoder.DescribeStream().TimeBase, Is.EqualTo(new Rational(1, 60)));
    Assert.That(second.PresentationTimestamp, Is.EqualTo(30));
    Assert.That(second.Duration, Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void RgbInputIsRefusedRatherThanSilentlyQuantised() {
    var encoder = AnimVideoEncoder.Create(_Stream(1, 1));
    var frame = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [1, 2, 3] };
    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(frame, 0, out _));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("ANIM"),
    Width = width,
    Height = height,
  };

  private static RawImage _Frame(int width, int height, byte[] paletteValues, byte[] pixels) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = pixels,
    Palette = _Palette(paletteValues),
    PaletteCount = paletteValues.Length,
  };

  private static byte[] _Palette(byte[] values) {
    var result = new byte[values.Length * 3];
    for (var i = 0; i < values.Length; ++i) {
      result[i * 3] = values[i];
      result[i * 3 + 1] = values[i];
      result[i * 3 + 2] = values[i];
    }
    return result;
  }

  private static bool _HasChunk(ReadOnlySpan<byte> packet, string id) {
    for (var i = 12; i + 8 <= packet.Length;) {
      if (packet[i] == id[0] && packet[i + 1] == id[1] && packet[i + 2] == id[2] && packet[i + 3] == id[3])
        return true;
      var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(i + 4, 4));
      if (length > int.MaxValue)
        return false;
      i += 8 + (int)length + ((int)length & 1);
    }
    return false;
  }
}
