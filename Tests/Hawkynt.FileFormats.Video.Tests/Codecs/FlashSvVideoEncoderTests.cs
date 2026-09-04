using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Flv;
using Hawkynt.FileFormats.Video;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

/// <summary>
/// The Flash Screen Video encoder measured against this package's own decoder: key frames that state
/// every cell, delta frames that leave unchanged cells empty, and a sequence that decodes back to
/// exactly the pictures it was given at sizes that are and are not whole cells.
/// </summary>
[TestFixture]
public sealed class FlashSvVideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 1000),
    FrameRate = new Rational(10, 1),
  };

  /// <summary>The two-byte block lengths of one packet, in the order the format writes them.</summary>
  private static int[] _BlockLengths(ReadOnlySpan<byte> packet) {
    var lengths = new System.Collections.Generic.List<int>();
    var offset = 4;
    while (offset < packet.Length) {
      var length = (packet[offset] << 8) | packet[offset + 1];
      lengths.Add(length);
      offset += 2 + length;
    }

    return [.. lengths];
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAcceptsAndCreates() {
    var encoder = FlashSvVideoEncoder.Create(_Stream(70, 37));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("FSV1")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("FSV1")));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(70));
      Assert.That(stream.Height, Is.EqualTo(37));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 1000)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10, 1)));
    });

    Assert.That(FlashSvVideoDecoder.Accepts(stream), Is.True);
    Assert.DoesNotThrow(() => FlashSvVideoDecoder.Create(stream));
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<FlashSvVideoDecoder>());
  }

  // ============================================================================================
  // Round trips through the registry's decoder
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
  public void RoundTripsASequenceExactlyWithTheRightKeyFrames(int width, int height, PixelFormat format) {
    const int _COUNT = 14;
    var frames = LosslessEncoderPictures.Sequence(width, height, format, _COUNT, seed: width * 7 + height);
    var encoder = FlashSvVideoEncoder.Create(_Stream(width, height));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < _COUNT; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i * 100, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      LosslessEncoderPictures.AssertSame(frames[i], decoded, $"frame {i}");

      var lengths = _BlockLengths(packet.Data.Span);
      var cells = ((width + 63) / 64) * ((height + 63) / 64);
      Assert.That(lengths, Has.Length.EqualTo(cells), $"frame {i}: one entry per cell");

      switch (i) {
        case 0:
          Assert.That(packet.IsKeyFrame, Is.True, "the first frame is a key frame");
          Assert.That(lengths, Is.All.Positive, "a key frame states every cell");
          break;
        case 6:
        case 13:
          Assert.That(packet.IsKeyFrame, Is.False, "a frame identical to the one before sends nothing");
          Assert.That(lengths, Is.All.Zero);
          break;
        default:
          Assert.That(packet.IsKeyFrame, Is.EqualTo(lengths.All(static length => length > 0)),
            $"frame {i}: a frame is a key frame exactly when it left no cell empty");
          break;
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void ForcesAKeyFrameEveryTwelfthFrame() {
    var picture = LosslessEncoderPictures.Noise(70, 37, PixelFormat.Bgr24, seed: 12);
    var encoder = FlashSvVideoEncoder.Create(_Stream(70, 37));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < 26; ++i) {
      // Every frame after the first touches one pixel in the left cell only, so nothing but the
      // interval could ever make a frame restate the right one.
      if (i > 0)
        picture = LosslessEncoderPictures.Patched(picture, 1, 1, 1, 1, seed: i);

      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      var lengths = _BlockLengths(packet.Data.Span);
      Assert.That(packet.IsKeyFrame, Is.EqualTo(i % 12 == 0), $"frame {i}");
      Assert.That(lengths[1] > 0, Is.EqualTo(i % 12 == 0), $"frame {i}: the untouched cell is sent on key frames only");
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      LosslessEncoderPictures.AssertSame(picture, decoded, $"frame {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void HeaderStatesTheGridAndADeltaFrameSendsOnlyTheChangedCell() {
    var first = LosslessEncoderPictures.Noise(70, 37, PixelFormat.Bgr24, seed: 9);
    // A patch entirely inside the right cell, the partial one.
    var second = LosslessEncoderPictures.Patched(first, 65, 2, 3, 3, seed: 10);
    var encoder = FlashSvVideoEncoder.Create(_Stream(70, 37));

    Assert.That(encoder.TryEncode(first, 0, out var key), Is.True);
    Assert.That(key.Data.Span[..4].ToArray(), Is.EqualTo(new byte[] { 0x30, 70, 0x30, 37 }), "64-pixel cells, 70x37 picture");

    Assert.That(encoder.TryEncode(second, 1, out var delta), Is.True);
    Assert.That(delta.IsKeyFrame, Is.False);

    // 70x37 in 64-pixel cells is one grid row of two cells; the patch lies in the partial right one.
    var lengths = _BlockLengths(delta.Data.Span);
    Assert.That(lengths, Has.Length.EqualTo(2));
    Assert.That(lengths[0], Is.Zero, "the left cell is unchanged and left empty");
    Assert.That(lengths[1], Is.Positive, "the right cell changed and is sent");
  }

  [Test]
  [Category("Unit")]
  public void PassesTimestampsThrough() {
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 5);
    var encoder = FlashSvVideoEncoder.Create(_Stream(4, 4));

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
    var encoder = FlashSvVideoEncoder.Create(_Stream(70, 37));
    var packets = frames.Select((frame, i) => {
      Assert.That(encoder.TryEncode(frame, i * 100, out var packet), Is.True);
      return packet;
    }).ToArray();

    var flv = VideoIO.Mux<FlvWriter>([encoder.DescribeStream()], packets);
    var container = FlvContainer.FromBytes(flv);
    var stream = FlvContainer.Streams(container).Single();
    var decoded = VideoIO.Decode(FlvContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder).ToArray();

    Assert.That(decoded, Has.Length.EqualTo(5));
    for (var i = 0; i < decoded.Length; ++i)
      LosslessEncoderPictures.AssertSame(frames[i], decoded[i].Image, $"frame {i}");
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.Throws<NotSupportedException>(() => FlashSvVideoEncoder.Create(_Stream(4, 4, MediaStreamKind.Audio)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<NotSupportedException>(() => FlashSvVideoEncoder.Create(_Stream(4, 0)));
    Assert.That(failure!.Message, Does.Contain("4x0"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWiderThanTwelveBits() {
    var failure = Assert.Throws<NotSupportedException>(() => FlashSvVideoEncoder.Create(_Stream(4096, 4)));
    Assert.That(failure!.Message, Does.Contain("4095"));
    Assert.DoesNotThrow(() => FlashSvVideoEncoder.Create(_Stream(4095, 4)));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void RefusesAPictureThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = FlashSvVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMidStreamGeometryChange() {
    var encoder = FlashSvVideoEncoder.Create(_Stream(8, 8));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 1);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }
}
