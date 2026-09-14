using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Flv;
using Hawkynt.FileFormats.Video;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

/// <summary>
/// Flash Screen Video 2 writer coverage: lossless 24-bit keyblocks, row-range interblocks against the
/// last key frame, zero-length unchanged cells, the zero-height "return to key frame" case, registry
/// discovery, FLV muxing and forced key-frame cadence.
/// </summary>
[TestFixture]
public sealed class FlashSv2VideoEncoderTests {

  private readonly record struct _Block(byte[] Payload) {
    public int Length => this.Payload.Length;
  }

  private static MediaStreamInfo _Stream(int width, int height, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 1000),
    FrameRate = new Rational(10, 1),
  };

  private static _Block[] _Blocks(ReadOnlySpan<byte> packet) {
    var blocks = new List<_Block>();
    var offset = 5;
    while (offset < packet.Length) {
      var length = (packet[offset] << 8) | packet[offset + 1];
      offset += 2;
      blocks.Add(new(packet.Slice(offset, length).ToArray()));
      offset += length;
    }

    return [.. blocks];
  }

  private static RawImage _Solid(int width, int height, byte blue = 0, byte green = 0, byte red = 0) {
    var pixels = new byte[width * height * 3];
    for (var offset = 0; offset < pixels.Length; offset += 3) {
      pixels[offset] = blue;
      pixels[offset + 1] = green;
      pixels[offset + 2] = red;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = pixels };
  }

  private static RawImage _WithPixel(RawImage source, int x, int y, byte blue, byte green, byte red) {
    var pixels = (byte[])source.PixelData.Clone();
    var offset = (y * source.Width + x) * 3;
    pixels[offset] = blue;
    pixels[offset + 1] = green;
    pixels[offset + 2] = red;
    return new() { Width = source.Width, Height = source.Height, Format = source.Format, PixelData = pixels };
  }

  // ============================================================================================
  // DescribeStream / registry
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAcceptsAndRegistryDiscovers() {
    var encoder = FlashSv2VideoEncoder.Create(_Stream(70, 37));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("FSV2")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("FSV2")));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(70));
      Assert.That(stream.Height, Is.EqualTo(37));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 1000)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10, 1)));
    });

    Assert.That(FlashSv2VideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<FlashSv2VideoDecoder>());
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<FlashSv2VideoEncoder>());
  }

  // ============================================================================================
  // Round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(64, 64, PixelFormat.Bgr24)]
  [TestCase(70, 37, PixelFormat.Bgr24)]
  [TestCase(130, 65, PixelFormat.Bgr24)]
  [TestCase(3, 2, PixelFormat.Bgr24)]
  [TestCase(70, 37, PixelFormat.Rgb24)]
  [TestCase(70, 37, PixelFormat.Bgra32)]
  [TestCase(70, 37, PixelFormat.Indexed8)]
  public void RoundTripsASequenceExactly(int width, int height, PixelFormat format) {
    const int _COUNT = 14;
    var frames = LosslessEncoderPictures.Sequence(width, height, format, _COUNT, seed: width * 11 + height);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < _COUNT; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i * 100, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.EqualTo(i % 12 == 0), $"frame {i}");
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      LosslessEncoderPictures.AssertSame(frames[i], decoded, $"frame {i}");

      var blocks = _Blocks(packet.Data.Span);
      Assert.That(blocks, Has.Length.EqualTo(((width + 63) / 64) * ((height + 63) / 64)));
      if (packet.IsKeyFrame)
        Assert.That(blocks.Select(static block => block.Length), Is.All.Positive, $"frame {i}: keyblocks are complete");
    }
  }

  [Test]
  [Category("Unit")]
  public void AnIdenticalInterframeWritesZeroLengthBlocks() {
    var picture = LosslessEncoderPictures.Noise(70, 37, PixelFormat.Bgr24, seed: 4);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(70, 37));

    Assert.That(encoder.TryEncode(picture, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var packet), Is.True);

    Assert.That(packet.IsKeyFrame, Is.False);
    Assert.That(_Blocks(packet.Data.Span).Select(static block => block.Length), Is.All.Zero);
  }

  [Test]
  [Category("Unit")]
  public void AnInterblockCarriesOnlyTheRowsThatDifferFromTheKeyFrame() {
    var key = _Solid(16, 16);
    var changed = _WithPixel(key, 7, 2, 0x11, 0x22, 0x33);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(16, 16));
    var decoder = FlashSv2VideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(key, 0, out var keyPacket), Is.True);
    Assert.That(decoder.TryDecode(keyPacket, out _), Is.True);
    Assert.That(encoder.TryEncode(changed, 1, out var delta), Is.True);

    var block = _Blocks(delta.Data.Span).Single().Payload;
    Assert.Multiple(() => {
      Assert.That(block[0], Is.EqualTo(0x04), "24-bit BGR plus HasDiffBlocks");
      Assert.That(block[1], Is.EqualTo(13), "display row 2 is local bottom-up row 13");
      Assert.That(block[2], Is.EqualTo(1));
    });

    Assert.That(decoder.TryDecode(delta, out var decoded), Is.True);
    LosslessEncoderPictures.AssertSame(changed, decoded);
  }

  [Test]
  [Category("Unit")]
  public void ReturningToTheKeyFrameUsesAZeroHeightDiffRatherThanAnUnchangedBlock() {
    var key = _Solid(16, 16, blue: 0x10, green: 0x20, red: 0x30);
    var changed = _WithPixel(key, 5, 5, 0xAA, 0xBB, 0xCC);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(16, 16));
    var decoder = FlashSv2VideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(key, 0, out var keyPacket), Is.True);
    Assert.That(decoder.TryDecode(keyPacket, out _), Is.True);
    Assert.That(encoder.TryEncode(changed, 1, out var changedPacket), Is.True);
    Assert.That(decoder.TryDecode(changedPacket, out _), Is.True);
    Assert.That(encoder.TryEncode(key, 2, out var restoredPacket), Is.True);

    var block = _Blocks(restoredPacket.Data.Span).Single().Payload;
    Assert.That(block, Is.EqualTo(new byte[] { 0x04, 0x00, 0x00 }));

    Assert.That(decoder.TryDecode(restoredPacket, out var restored), Is.True);
    LosslessEncoderPictures.AssertSame(key, restored);
  }

  [Test]
  [Category("Unit")]
  public void ForcesAKeyFrameEveryTwelfthFrame() {
    var picture = _Solid(16, 16);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(16, 16));

    for (var i = 0; i < 26; ++i) {
      if (i > 0)
        picture = _WithPixel(picture, 0, 0, (byte)i, (byte)(i * 3), (byte)(i * 7));

      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.EqualTo(i % 12 == 0), $"frame {i}");

      var block = _Blocks(packet.Data.Span).Single().Payload;
      if (packet.IsKeyFrame)
        Assert.That((block[0] & 0x04), Is.Zero, $"frame {i}: a keyblock covers the whole cell");
      else
        Assert.That((block[0] & 0x04), Is.EqualTo(0x04), $"frame {i}: only one row differs from the key frame");
    }
  }

  // ============================================================================================
  // Container / timestamps
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PassesTimestampsThrough() {
    var picture = _Solid(4, 4);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(4, 4));

    Assert.That(encoder.TryEncode(picture, 1234, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(1234));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(1234));
    });

    Assert.That(encoder.TryEncode(picture, null, out packet), Is.True);
    Assert.That(packet.PresentationTimestamp, Is.Null);
    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MuxesIntoFlvAndDecodesBackThroughTheContainer() {
    var frames = LosslessEncoderPictures.Sequence(70, 37, PixelFormat.Bgr24, 5, seed: 21);
    var encoder = FlashSv2VideoEncoder.Create(_Stream(70, 37));
    var packets = frames.Select((frame, i) => {
      Assert.That(encoder.TryEncode(frame, i * 100, out var packet), Is.True);
      return packet;
    }).ToArray();

    var flv = VideoIO.Mux<FlvWriter>([encoder.DescribeStream()], packets);
    var container = FlvContainer.FromBytes(flv);
    var stream = FlvContainer.Streams(container).Single();
    var decoded = VideoIO.Decode(FlvContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder).ToArray();

    Assert.That(decoded, Has.Length.EqualTo(frames.Length));
    for (var i = 0; i < decoded.Length; ++i)
      LosslessEncoderPictures.AssertSame(frames[i], decoded[i].Image, $"frame {i}");
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.Throws<NotSupportedException>(() => FlashSv2VideoEncoder.Create(_Stream(4, 4, MediaStreamKind.Audio)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<NotSupportedException>(() => FlashSv2VideoEncoder.Create(_Stream(4, 0)));
    Assert.That(failure!.Message, Does.Contain("4x0"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWiderThanTwelveBits() {
    var failure = Assert.Throws<NotSupportedException>(() => FlashSv2VideoEncoder.Create(_Stream(4096, 4)));
    Assert.That(failure!.Message, Does.Contain("4095"));
    Assert.DoesNotThrow(() => FlashSv2VideoEncoder.Create(_Stream(4095, 4)));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void RefusesAPictureThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = FlashSv2VideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMidStreamGeometryChange() {
    var encoder = FlashSv2VideoEncoder.Create(_Stream(8, 8));
    var picture = _Solid(4, 4);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }
}
