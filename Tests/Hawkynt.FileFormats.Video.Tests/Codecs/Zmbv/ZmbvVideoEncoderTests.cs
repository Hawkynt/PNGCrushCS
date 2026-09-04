using System;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Zmbv.Tests;

/// <summary>
/// The ZMBV encoder measured against this package's own decoder — the one whose inflater carries a
/// zlib dictionary across packets, so a sequence decoding exactly is also the proof that each packet
/// ends where the decoder needs it to: on a sync-flush boundary of one continuing stream.
/// </summary>
[TestFixture]
public class ZmbvVideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel = 0, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 70),
    FrameRate = new Rational(70, 1),
  };

  private static void _RoundTrip(int width, int height, int bitsPerPixel, RawImage[] frames, Action<int, CodedPacket>? inspect = null) {
    var encoder = ZmbvVideoEncoder.Create(_Stream(width, height, bitsPerPixel));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    for (var i = 0; i < frames.Length; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.EqualTo(i % 25 == 0), $"frame {i}: the first frame and every twenty-fifth is an intraframe");
      Assert.That((packet.Data.Span[0] & 1) != 0, Is.EqualTo(packet.IsKeyFrame), $"frame {i}: the intra bit agrees with the flag");
      inspect?.Invoke(i, packet);

      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      LosslessEncoderPictures.AssertSame(frames[i], decoded, $"frame {i}");
    }
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(0, 32)]
  [TestCase(32, 32)]
  [TestCase(16, 16)]
  [TestCase(8, 8)]
  public void DescribesAStreamTheDecoderAcceptsAndCreates(int requestedBits, int describedBits) {
    var encoder = ZmbvVideoEncoder.Create(_Stream(33, 17, requestedBits));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("ZMBV")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("ZMBV")));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(33));
      Assert.That(stream.Height, Is.EqualTo(17));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(describedBits));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 70)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(70, 1)));
    });

    Assert.That(ZmbvVideoDecoder.Accepts(stream), Is.True);
    Assert.DoesNotThrow(() => ZmbvVideoDecoder.Create(stream));
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<ZmbvVideoDecoder>());
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(16, 16, PixelFormat.Bgra32)]
  [TestCase(33, 17, PixelFormat.Bgra32)]
  [TestCase(40, 24, PixelFormat.Bgra32)]
  [TestCase(5, 3, PixelFormat.Bgra32)]
  [TestCase(33, 17, PixelFormat.Bgr24)]
  [TestCase(33, 17, PixelFormat.Rgb24)]
  [TestCase(33, 17, PixelFormat.Gray8)]
  [TestCase(33, 17, PixelFormat.Indexed8)]
  public void RoundTripsA32BitSequenceExactly(int width, int height, PixelFormat format) {
    var frames = LosslessEncoderPictures.Sequence(width, height, format, 9, seed: width * 3 + height);
    _RoundTrip(width, height, 32, frames, (i, packet) => {
      if (i == 0)
        Assert.That(packet.Data.Span[..7].ToArray(), Is.EqualTo(new byte[] { 1, 0, 1, 1, 8, 16, 16 }), "version 0.1, zlib, 32-bit, 16x16 blocks");
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase(16, 16)]
  [TestCase(33, 17)]
  [TestCase(5, 3)]
  public void RoundTripsA16BitSequenceExactly(int width, int height) {
    var frames = LosslessEncoderPictures.Sequence(width, height, PixelFormat.Rgb565, 9, seed: width + height);
    _RoundTrip(width, height, 16, frames, (i, packet) => {
      if (i == 0)
        Assert.That(packet.Data.Span[4], Is.EqualTo(6), "format 6 is 5-6-5");
      Assert.That(packet.Data.Span[0] & 2, Is.Zero, "no palette to change");
    });
  }

  [Test]
  [Category("Unit")]
  [TestCase(16, 16)]
  [TestCase(33, 17)]
  [TestCase(5, 3)]
  public void RoundTripsAPalettisedSequenceExactlyIncludingAPaletteChange(int width, int height) {
    var frames = LosslessEncoderPictures.Sequence(width, height, PixelFormat.Indexed8, 9, seed: width * 5 + height);
    var repainted = LosslessEncoderPictures.Repainted(frames[3], seed: 77).Palette;
    for (var i = 3; i < frames.Length; ++i)
      frames[i] = LosslessEncoderPictures.With(frames[i], palette: repainted);

    _RoundTrip(width, height, 8, frames, (i, packet) => {
      if (i == 0)
        Assert.That(packet.Data.Span[4], Is.EqualTo(4), "format 4 is 8-bit palettised");
      Assert.That((packet.Data.Span[0] & 2) != 0, Is.EqualTo(i == 3), $"frame {i}: the palette-change bit marks exactly the frame whose palette differs from the last");
    });
  }

  [Test]
  [Category("Unit")]
  public void ForcesAnIntraframeEveryTwentyFifthFrameAndRestartsTheZlibStream() {
    var frames = LosslessEncoderPictures.Sequence(16, 16, PixelFormat.Bgra32, 52, seed: 4);
    _RoundTrip(16, 16, 32, frames, (i, packet) => {
      if (i is 25 or 50)
        Assert.That(packet.Data.Span[..7].ToArray(), Is.EqualTo(new byte[] { 1, 0, 1, 1, 8, 16, 16 }), $"frame {i} restates the header");
      if (packet.IsKeyFrame)
        Assert.That(packet.Data.Span[7] & 0x0F, Is.EqualTo(8), $"frame {i}: a fresh zlib header follows an intraframe's own header");
    });
  }

  [Test]
  [Category("Unit")]
  public void AShiftedPictureIsCodedAsMotionVectorsRatherThanRestated() {
    var first = LosslessEncoderPictures.Noise(64, 64, PixelFormat.Bgra32, seed: 8);
    var shifted = LosslessEncoderPictures.Shifted(first, 3, -2, seed: 9);
    var encoder = ZmbvVideoEncoder.Create(_Stream(64, 64, 32));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(first, 0, out var intra), Is.True);
    Assert.That(encoder.TryEncode(shifted, 1, out var inter), Is.True);
    Assert.That(inter.Data.Length, Is.LessThan(intra.Data.Length / 3),
      "noise does not compress, so an interframe this small can only have copied its blocks from the frame before");

    Assert.That(decoder.TryDecode(intra, out _), Is.True);
    Assert.That(decoder.TryDecode(inter, out var decoded), Is.True);
    LosslessEncoderPictures.AssertSame(shifted, decoded, "the shifted frame");
  }

  [Test]
  [Category("Unit")]
  public void PassesTimestampsThrough() {
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgra32, seed: 5);
    var encoder = ZmbvVideoEncoder.Create(_Stream(4, 4));

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
  public void MuxesIntoAviAndDecodesBackThroughTheContainer() {
    var frames = LosslessEncoderPictures.Sequence(33, 17, PixelFormat.Indexed8, 6, seed: 31);
    var encoder = ZmbvVideoEncoder.Create(_Stream(33, 17, 8));
    var packets = frames.Select((frame, i) => {
      Assert.That(encoder.TryEncode(frame, i, out var packet), Is.True);
      return packet;
    }).ToArray();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoded = VideoIO.Decode(AviContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder).ToArray();

    Assert.That(decoded, Has.Length.EqualTo(6));
    for (var i = 0; i < decoded.Length; ++i)
      LosslessEncoderPictures.AssertSame(frames[i], decoded[i].Image, $"frame {i}");
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.Throws<NotSupportedException>(() => ZmbvVideoEncoder.Create(_Stream(4, 4, kind: MediaStreamKind.Audio)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<NotSupportedException>(() => ZmbvVideoEncoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(15)]
  [TestCase(24)]
  [TestCase(1)]
  public void RefusesABitDepthTheFormatHasNoLosslessLayoutFor(int bitsPerPixel) {
    var failure = Assert.Throws<NotSupportedException>(() => ZmbvVideoEncoder.Create(_Stream(4, 4, bitsPerPixel)));
    Assert.That(failure!.Message, Does.Contain($"{bitsPerPixel} bits"));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedStreamRefusesAPictureWithoutAPalette() {
    var encoder = ZmbvVideoEncoder.Create(_Stream(4, 4, 8));

    var colour = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 1);
    var refused = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(colour, 0, out _));
    Assert.That(refused!.Message, Does.Contain("Bgr24"));

    var missing = LosslessEncoderPictures.With(LosslessEncoderPictures.Noise(4, 4, PixelFormat.Indexed8, seed: 1), dropPalette: true);
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(missing, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void A16BitStreamRefusesAnythingButRgb565() {
    var encoder = ZmbvVideoEncoder.Create(_Stream(4, 4, 16));
    var colour = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgr24, seed: 1);

    var refused = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(colour, 0, out _));
    Assert.That(refused!.Message, Does.Contain("Bgr24"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void A32BitStreamRefusesAPictureThatCannotBecomeEightBitRgbLosslessly(PixelFormat format) {
    var encoder = ZmbvVideoEncoder.Create(_Stream(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMidStreamGeometryChange() {
    var encoder = ZmbvVideoEncoder.Create(_Stream(8, 8));
    var picture = LosslessEncoderPictures.Noise(4, 4, PixelFormat.Bgra32, seed: 1);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }
}
