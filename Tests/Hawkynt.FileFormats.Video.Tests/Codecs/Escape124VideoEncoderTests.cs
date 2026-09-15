using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using FileFormat.Rpl;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class Escape124VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheEncoder() {
    var stream = _Stream(8, 8);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.AllEncoders.Select(encoder => encoder.CodecName), Does.Contain("Escape 124"));
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Escape124VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void StreamDescriptionUsesTheRplCodecNumberAndRgbDepth() {
    var requested = _Stream(8, 8);
    var encoder = Escape124VideoEncoder.Create(requested);
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Index, Is.EqualTo(0));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Codec, Is.EqualTo(new CodecTag(124)));
      Assert.That(described.Handler, Is.EqualTo(new CodecTag(124)));
      Assert.That(described.Width, Is.EqualTo(8));
      Assert.That(described.Height, Is.EqualTo(8));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void TwoColourMacroblocksRoundTripExactlyOnTheRgb555Grid() {
    var stream = _Stream(8, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var decoder = Escape124VideoDecoder.Create(stream);
    var source = _Checkerboard(8, 8, (255, 0, 0), (0, 0, 255));

    Assert.That(encoder.TryEncode(source, 7, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
    Assert.That(
      BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span[4..8]),
      Is.EqualTo(checked((uint)packet.Data.Length)));

    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void IdenticalPictureUsesTheEightByteRepeatFrame() {
    var stream = _Stream(8, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var decoder = Escape124VideoDecoder.Create(stream);
    var source = _Solid(8, 8, 255, 0, 0);

    Assert.That(encoder.TryEncode(source, 0, out var first), Is.True);
    Assert.That(decoder.TryDecode(first, out var firstDecoded), Is.True);
    Assert.That(encoder.TryEncode(source, 1, out var repeat), Is.True);

    Assert.Multiple(() => {
      Assert.That(repeat.IsKeyFrame, Is.False);
      Assert.That(repeat.Data.Length, Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(repeat.Data.Span[..4]), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(repeat.Data.Span[4..8]), Is.EqualTo(8u));
    });

    Assert.That(decoder.TryDecode(repeat, out var repeated), Is.True);
    Assert.That(repeated.PixelData, Is.EqualTo(firstDecoded.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PartialChangeReusesTheUnchangedPreviousSuperblock() {
    var stream = _Stream(16, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var decoder = Escape124VideoDecoder.Create(stream);
    var firstSource = _Split(16, 8, (255, 0, 0), (0, 0, 255));
    var secondSource = _Split(16, 8, (255, 0, 0), (0, 255, 0));

    Assert.That(encoder.TryEncode(firstSource, 0, out var first), Is.True);
    Assert.That(first.IsKeyFrame, Is.True);
    Assert.That(decoder.TryDecode(first, out _), Is.True);

    Assert.That(encoder.TryEncode(secondSource, 1, out var second), Is.True);
    Assert.That(second.IsKeyFrame, Is.False);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(secondSource.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void SkipRunsBeyondTheLargestCodeForceOneRefreshAndStaySynchronized() {
    const int superblocks = 4232;
    var width = superblocks * 8;
    var stream = _Stream(width, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var decoder = Escape124VideoDecoder.Create(stream);
    var firstSource = _Solid(width, 8, 255, 0, 0);
    var secondSource = _Solid(width, 8, 255, 0, 0);

    for (var y = 0; y < 8; ++y)
    for (var x = width - 8; x < width; ++x) {
      var at = (y * width + x) * 3;
      secondSource.PixelData[at] = 0;
      secondSource.PixelData[at + 1] = 0;
      secondSource.PixelData[at + 2] = 255;
    }

    Assert.That(encoder.TryEncode(firstSource, 0, out var first), Is.True);
    Assert.That(decoder.TryDecode(first, out _), Is.True);
    Assert.That(encoder.TryEncode(secondSource, 1, out var second), Is.True);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(secondSource.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FourColourMacroblockUsesTheBestRepresentablePairAndRemainsDecodable() {
    var stream = _Stream(8, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var decoder = Escape124VideoDecoder.Create(stream);
    var source = _Solid(8, 8, 0, 0, 0);

    source.PixelData[0] = 255;
    source.PixelData[1] = 0;
    source.PixelData[2] = 0;
    source.PixelData[3] = 0;
    source.PixelData[4] = 255;
    source.PixelData[5] = 0;
    source.PixelData[8 * 3] = 0;
    source.PixelData[8 * 3 + 1] = 0;
    source.PixelData[8 * 3 + 2] = 255;

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(8));
      Assert.That(decoded.Height, Is.EqualTo(8));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(8 * 8 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void RplOracleClipMatchesTheExternallyDecodedKnownAnswer() {
    var file = _OracleClip();
    var digest = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();

    Assert.That(digest, Is.EqualTo("bb27e02f869645a5baea155925e93ef3bb81b5a81a97feca41c6f89b4727a166"));
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsTheKeyDeltaAndRepeatFramesWrittenHere() {
    FFmpegOracle.RequireAvailable();

    var file = _OracleClip();
    var path = Path.Combine(Path.GetTempPath(), $"escape124-{Guid.NewGuid():N}.rpl");
    try {
      File.WriteAllBytes(path, file);
      var (decoded, output) = FFmpegOracle.TryDecodeFrameCount(path, 16, 8, 3);
      Assert.That(decoded, Is.True, output);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void PartialEdgeSuperblocksAreRefusedByTheEncoderToo()
    => Assert.Throws<NotSupportedException>(() => Escape124VideoEncoder.Create(_Stream(10, 8)));

  [Test]
  [Category("Unit")]
  public void AShortSourceBufferIsRefused() {
    var encoder = Escape124VideoEncoder.Create(_Stream(8, 8));
    var frame = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[8],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }

  private static byte[] _OracleClip() {
    var stream = _Stream(16, 8);
    var encoder = Escape124VideoEncoder.Create(stream);
    var first = _Solid(16, 8, 255, 0, 0);
    var changed = _Split(16, 8, (255, 0, 0), (0, 255, 0));
    var packets = new List<CodedPacket>(3);

    Assert.That(encoder.TryEncode(first, 0, out var keyFrame), Is.True);
    Assert.That(encoder.TryEncode(changed, 1, out var deltaFrame), Is.True);
    Assert.That(encoder.TryEncode(changed, 2, out var repeatFrame), Is.True);
    packets.Add(keyFrame);
    packets.Add(deltaFrame);
    packets.Add(repeatFrame);

    return VideoIO.Mux<RplWriter>([encoder.DescribeStream()], packets);
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = new CodecTag(124),
    Handler = new CodecTag(124),
    Width = width,
    Height = height,
    BitsPerPixel = 16,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Solid(int width, int height, byte red, byte green, byte blue) {
    var data = new byte[width * height * 3];
    for (var at = 0; at < data.Length; at += 3) {
      data[at] = red;
      data[at + 1] = green;
      data[at + 2] = blue;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static RawImage _Checkerboard(
    int width, int height,
    (byte R, byte G, byte B) first,
    (byte R, byte G, byte B) second) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = ((x + y) & 1) == 0 ? first : second;
      var at = (y * width + x) * 3;
      data[at] = colour.R;
      data[at + 1] = colour.G;
      data[at + 2] = colour.B;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static RawImage _Split(
    int width, int height,
    (byte R, byte G, byte B) left,
    (byte R, byte G, byte B) right) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = x < width / 2 ? left : right;
      var at = (y * width + x) * 3;
      data[at] = colour.R;
      data[at + 1] = colour.G;
      data[at + 2] = colour.B;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }
}
