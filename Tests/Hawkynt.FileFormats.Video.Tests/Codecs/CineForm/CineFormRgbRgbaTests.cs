using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

[TestFixture]
public sealed class CineFormRgbRgbaTests {
  private static readonly CodecTag _Cfhd = CodecTag.FromCharacters("CFHD");

  [Test]
  [Category("Unit")]
  public void RgbWriterUsesTheMeasuredTwelveBitHeaderAndChannelOrder() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.Rgb444);
    var frame = _Rgb48(WIDTH, HEIGHT, r: 1024, g: 2048, b: 3072);

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ChannelCount), Is.EqualTo(3));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.EncodedFormat), Is.EqualTo(3));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.Precision), Is.EqualTo(12));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.PrescaleTable), Is.EqualTo(0x2800));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(36));
      Assert.That(decoded.EncodedFormat, Is.EqualTo(CineFormEncodedFormat.Rgb444));
      Assert.That(decoded.Precision, Is.EqualTo(12));
      Assert.That(decoded.IsYuv, Is.False);
      Assert.That(decoded.Channels[0].Samples.Take(WIDTH * HEIGHT), Is.All.EqualTo(2048), "G is channel 0");
      Assert.That(decoded.Channels[1].Samples.Take(WIDTH * HEIGHT), Is.All.EqualTo(1024), "R is channel 1");
      Assert.That(decoded.Channels[2].Samples.Take(WIDTH * HEIGHT), Is.All.EqualTo(3072), "B is channel 2");
    });
  }

  [Test]
  [Category("Unit")]
  public void RgbaWriterUsesFourChannelsAndRoundTripsCompandedAlpha() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    ushort[] alphaValues = [0, 256, 1024, 2048, 3072, 4000, 4095];

    foreach (var alpha in alphaValues) {
      var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.Rgba4444);
      var frame = _Rgba64(WIDTH, HEIGHT, r: 1000, g: 2000, b: 3000, a: alpha);

      Assert.That(encoder.TryEncode(frame, alpha, out var packet), Is.True);
      var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);
      var worstAlphaError = decoded.Channels[3].Samples
        .Take(WIDTH * HEIGHT)
        .Max(sample => Math.Abs(sample - alpha));

      Assert.Multiple(() => {
        Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ChannelCount), Is.EqualTo(4));
        Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.EncodedFormat), Is.EqualTo(4));
        Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.Precision), Is.EqualTo(12));
        Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.PrescaleTable), Is.EqualTo(0x2800));
        Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(48));
        Assert.That(decoded.EncodedFormat, Is.EqualTo(CineFormEncodedFormat.Rgba4444));
        Assert.That(decoded.HasAlpha, Is.True);
        Assert.That(worstAlphaError, Is.LessThanOrEqualTo(3), $"alpha {alpha}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void PublicDecoderReturnsRgba32ForAnAlphaFrame() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.Rgba4444);
    Assert.That(encoder.TryEncode(_Rgba64(WIDTH, HEIGHT, 4095, 2048, 1024, 3072), 1, out var packet), Is.True);

    var decoder = CineFormVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(frame.Width, Is.EqualTo(WIDTH));
      Assert.That(frame.Height, Is.EqualTo(HEIGHT));
      Assert.That(frame.PixelData.Length, Is.EqualTo(WIDTH * HEIGHT * 4));
      Assert.That(frame.PixelData[3], Is.InRange(190, 192));
    });
  }

  [Test]
  [Category("Unit")]
  public void BayerIsRefusedByNameInsteadOfBeingMisreadAsRgba() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.Rgba4444);
    var packet = encoder.EncodeFrame(_Rgba64(WIDTH, HEIGHT, 1000, 2000, 3000, 4095));
    _PatchHeaderValue(packet, CineFormTags.EncodedFormat, (ushort)CineFormEncodedFormat.Bayer);

    var decoder = CineFormVideoDecoder.Create(encoder.DescribeStream());
    var failure = Assert.Throws<NotSupportedException>(() => decoder.DecodeChannels(packet));
    Assert.That(failure!.Message, Does.Contain("Bayer"));
  }

  [Test]
  [Category("Unit")]
  public void FormatAndChannelCountMustAgree() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.Rgb444);
    var packet = encoder.EncodeFrame(_Rgb48(WIDTH, HEIGHT, 1000, 2000, 3000));
    _PatchHeaderValue(packet, CineFormTags.EncodedFormat, (ushort)CineFormEncodedFormat.Rgba4444);

    var decoder = CineFormVideoDecoder.Create(encoder.DescribeStream());
    Assert.Throws<InvalidDataException>(() => decoder.DecodeChannels(packet));
  }

  [TestCase(CineFormEncodingFormat.Rgb444)]
  [TestCase(CineFormEncodingFormat.Rgba4444)]
  [Category("Conformance")]
  public void FfmpegReadsTheNewTwelveBitLayoutsWhenItIsAvailable(CineFormEncodingFormat encodingFormat) {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(width, height), encodingFormat);
    var frame = encodingFormat == CineFormEncodingFormat.Rgba4444
      ? _Rgba64(width, height, 3072, 1536, 768, 2048)
      : _Rgb48(width, height, 3072, 1536, 768);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, output) = FFmpegOracle.TryDecodeFirstFrame(path, width, height);
      Assert.That(decoded, Is.True, output);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    // Zero, because these go through the AVI writer and an AVI's stream index is not a label: it is
    // written into every chunk identifier, so the streams have to run densely from nought.
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = _Cfhd,
    Handler = _Cfhd,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Rgb48(int width, int height, ushort r, ushort g, ushort b)
    => _HighDepthRgb(width, height, [r, g, b], PixelFormat.Rgb48);

  private static RawImage _Rgba64(int width, int height, ushort r, ushort g, ushort b, ushort a)
    => _HighDepthRgb(width, height, [r, g, b, a], PixelFormat.Rgba64);

  private static RawImage _HighDepthRgb(int width, int height, ushort[] twelveBit, PixelFormat format) {
    var components = twelveBit.Length;
    var data = new byte[width * height * components * 2];
    var offset = 0;
    for (var pixel = 0; pixel < width * height; ++pixel)
    foreach (var sample in twelveBit) {
      BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), _Expand12To16(sample));
      offset += 2;
    }

    return new() { Width = width, Height = height, Format = format, PixelData = data };
  }

  private static ushort _Expand12To16(ushort value) {
    if (value > 4095)
      throw new ArgumentOutOfRangeException(nameof(value));
    return (ushort)((value * 65535 + 2047) / 4095);
  }

  private static ushort _HeaderValue(ReadOnlySpan<byte> packet, int wantedTag) {
    var position = 0;
    while (position + 4 <= packet.Length) {
      var tag = (short)BinaryPrimitives.ReadUInt16BigEndian(packet[position..]);
      var value = BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 2)..]);
      position += 4;
      if (tag == wantedTag)
        return value;
      if (tag == CineFormTags.Index) {
        var bytes = checked(value * 4);
        if (position > packet.Length - bytes)
          throw new InvalidDataException("CineForm test packet ended inside its channel index.");
        position += bytes;
      }
      if (tag == CineFormTags.LowpassPrecision)
        break;
    }

    throw new InvalidDataException($"CineForm test packet did not contain header tag {wantedTag}.");
  }

  private static void _PatchHeaderValue(byte[] packet, int wantedTag, ushort replacement) {
    var position = 0;
    while (position + 4 <= packet.Length) {
      var tag = (short)BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(position));
      var value = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(position + 2));
      if (tag == wantedTag) {
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(position + 2), replacement);
        return;
      }

      position += 4;
      if (tag == CineFormTags.Index)
        position += checked(value * 4);
      if (tag == CineFormTags.LowpassPrecision)
        break;
    }

    throw new InvalidDataException($"CineForm test packet did not contain header tag {wantedTag}.");
  }
}
