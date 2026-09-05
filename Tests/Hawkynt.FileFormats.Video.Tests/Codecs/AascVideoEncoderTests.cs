using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The AASC encoder, checked by decoding what it writes with the decoder beside it.
/// </summary>
/// <remarks>
/// The coding is lossless, so the whole of the contract is that the picture comes back exactly — over
/// a sequence, so that the reposition escapes are reached, and over pictures shaped so that each of
/// the two frame forms is chosen: runs and literals where they pay, and the uncompressed form where
/// coding noise would cost more than stating it.
/// <para/>
/// What ties this to anything outside the repository is a measurement rather than a test: the frames
/// written here were muxed into an AVI and handed to ffmpeg, whose <c>bgr24</c> output was compared
/// sample for sample against the pictures that went in. Forty-eight sequences of five frames — widths
/// 1, 3, 7, 17, 63, 64, 65 and 320 against flat, noisy, moving, unchanging, gradient and
/// run-and-noise content — came back with no differing sample, over streams reaching every opcode
/// including runs and literal runs longer than one can state and repositions of a full 255 bytes.
/// </remarks>
[TestFixture]
public sealed class AascVideoEncoderTests {

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAcceptsAndCreates() {
    var encoder = AascVideoEncoder.Create(_Requested(20, 12));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("AASC")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("AASC")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(20), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(12), "biHeight, positive for bottom-up");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24), "biBitCount");
      Assert.That(format[16..20], Is.EqualTo("AASC"u8.ToArray()), "biCompression");
    });

    Assert.That(AascVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AascVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegistered() {
    var requested = _Requested(8, 4, codec: "AASC");

    Assert.That(
      VideoFormatRegistry.AllEncoders.Select(e => e.CodecName),
      Does.Contain("Autodesk Animator Codec"));
    Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<AascVideoEncoder>());
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASequenceRoundTripsExactly([Values(1, 3, 7, 63, 64, 320)] int width) {
    var height = width switch { 1 => 1, 3 => 2, 7 => 5, 63 => 9, 64 => 8, _ => 21 };
    var pictures = LosslessEncoderPictures.Sequence(width, height, PixelFormat.Bgr24, 8, seed: width);

    _AssertRoundTrip(width, height, pictures);
  }

  [Test]
  [Category("Unit")]
  public void RunsAndLiteralsStayRunLengthCodedAndRoundTrip([Values(63, 320, 1000)] int width) {
    var height = width switch { 63 => 9, 320 => 11, _ => 3 };
    var pictures = _RunsAndNoise(width, height, 4, seed: width);

    var packets = _AssertRoundTrip(width, height, pictures);
    Assert.That(
      packets.Select(p => BinaryPrimitives.ReadUInt32LittleEndian(p.Data.Span)),
      Is.All.EqualTo(1u),
      "content with runs in it is worth coding, so no frame falls back to the uncompressed form");
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsAllOneColourIsCodedAsRuns() {
    var flat = _Flat(8, 4, 0x40);
    var encoder = AascVideoEncoder.Create(_Requested(8, 4));

    Assert.That(encoder.TryEncode(flat, 0, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      // Four rows of one twenty-four-byte run and an end-of-row, behind the compression word, then
      // the frame's end.
      Assert.That(
        packet.Data.ToArray(),
        Is.EqualTo(new byte[] {
          1, 0, 0, 0,
          24, 0x40, 0, 0,
          24, 0x40, 0, 0,
          24, 0x40, 0, 0,
          24, 0x40, 0, 0,
          0, 1,
        }));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureIdenticalToTheOneBeforeIsOneVerticalRepositionAndNotAKeyFrame() {
    var encoder = AascVideoEncoder.Create(_Requested(16, 8));
    var picture = _Flat(16, 8, 0x77);

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      // 00 02 00 07 steps seven rows, the end-of-row is the eighth, and 00 01 ends the frame.
      Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 1, 0, 0, 0, 0, 2, 0, 7, 0, 0, 0, 1 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void NoiseIsWrittenUncompressedAndStaysAKeyFrame() {
    var pictures = new[] {
      LosslessEncoderPictures.Noise(64, 8, PixelFormat.Bgr24, seed: 5),
      LosslessEncoderPictures.Noise(64, 8, PixelFormat.Bgr24, seed: 6),
    };

    var packets = _AssertRoundTrip(64, 8, pictures);
    Assert.Multiple(() => {
      foreach (var packet in packets) {
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span), Is.EqualTo(0u), "the uncompressed form");
        Assert.That(packet.Data.Length, Is.EqualTo(4 + 64 * 3 * 8), "a stride of 192 needs no padding");
        Assert.That(packet.IsKeyFrame, Is.True, "an uncompressed frame states every byte");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void AnUncompressedFrameOfAnOddWidthPadsEveryRowToAWord() {
    var picture = LosslessEncoderPictures.Noise(7, 3, PixelFormat.Bgr24, seed: 9);

    var packets = _AssertRoundTrip(7, 3, [picture]);
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packets[0].Data.Span), Is.EqualTo(0u));
    Assert.That(packets[0].Data.Length, Is.EqualTo(4 + 24 * 3), "a stride of 21 is padded to 24");
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitLayoutThatIsNotBgrIsConvertedRatherThanRefused() {
    var pictures = new[] {
      LosslessEncoderPictures.Noise(9, 4, PixelFormat.Rgba32, seed: 2),
      LosslessEncoderPictures.Noise(9, 4, PixelFormat.Rgba32, seed: 3),
    };

    _AssertRoundTrip(9, 4, pictures);
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAnAviAndComeBackThroughTheRegistry() {
    var pictures = _RunsAndNoise(37, 14, 5, seed: 21);
    var encoder = AascVideoEncoder.Create(_Requested(37, 14));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("AASC")));

    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(f => f.Image).ToList();
    Assert.That(decoded.Count, Is.EqualTo(pictures.Count));
    for (var i = 0; i < pictures.Count; ++i)
      LosslessEncoderPictures.AssertSame(pictures[i], decoded[i], $"frame {i}");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = AascVideoEncoder.Create(_Requested(4, 4, index: 3));
    var picture = _Flat(4, 4, 0x11);

    Assert.That(encoder.TryEncode(picture, 42, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.EqualTo(3));
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack() {
    var encoder = AascVideoEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Flat(4, 4, 0x22), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused()
    => Assert.Throws<NotSupportedException>(() => AascVideoEncoder.Create(_Requested(4, 4, kind: MediaStreamKind.Audio)));

  [Test]
  [Category("Unit")]
  public void APictureWithNoPixelsIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => AascVideoEncoder.Create(_Requested(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthTheCodingIsNotReadAtIsRefusedByName([Values(8, 16, 32)] int bitsPerPixel) {
    var failure = Assert.Throws<NotSupportedException>(() => AascVideoEncoder.Create(_Requested(4, 4, bitsPerPixel: bitsPerPixel)));
    Assert.That(failure!.Message, Does.Contain($"{bitsPerPixel} bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(PixelFormat.Rgb48)]
  [TestCase(PixelFormat.RgbF32)]
  [TestCase(PixelFormat.Yuv420P8)]
  public void APictureThatCannotBecomeEightBitColourLosslesslyIsRefusedByName(PixelFormat format) {
    var encoder = AascVideoEncoder.Create(_Requested(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 16] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = AascVideoEncoder.Create(_Requested(8, 8));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(4, 4, 0x33), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static IReadOnlyList<CodedPacket> _AssertRoundTrip(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = AascVideoEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
    }

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder, Is.InstanceOf<AascVideoDecoder>());

    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      LosslessEncoderPictures.AssertSame(pictures[i], decoded, $"frame {i}");
    }

    Assert.That(packets[0].IsKeyFrame, Is.True, "the first frame is written whole");
    return packets;
  }

  /// <summary>A picture of one byte value repeated, which is one run an row and nothing else.</summary>
  private static RawImage _Flat(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = pixels };
  }

  /// <summary>
  /// Pictures of long runs and noisy stretches alternating, which is the content the coding was
  /// meant for: runs longer than one opcode can state, literal runs longer than one opcode can state,
  /// odd literal counts needing their padding byte, and enough left unchanged between frames for the
  /// reposition escapes to be worth writing.
  /// </summary>
  private static IReadOnlyList<RawImage> _RunsAndNoise(int width, int height, int count, int seed) {
    var random = new Random(seed);
    var frames = new List<RawImage>(count);
    var pixels = new byte[width * height * 3];
    for (var frame = 0; frame < count; ++frame) {
      // Every frame but the first keeps most of the one before, so that the parts that did change are
      // written and the parts that did not are stepped over.
      var from = frame == 0 ? 0 : random.Next(pixels.Length);
      for (var i = from; i < pixels.Length;) {
        var length = random.Next(1, 400);
        if (random.Next(2) == 0) {
          var value = (byte)random.Next(0, 16);
          for (var n = 0; n < length && i < pixels.Length; ++n, ++i)
            pixels[i] = value;
        } else
          for (var n = 0; n < length && i < pixels.Length; ++n, ++i)
            pixels[i] = (byte)random.Next(0, 16);
      }

      frames.Add(new() {
        Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = (byte[])pixels.Clone(),
      });
    }

    return frames;
  }

  private static MediaStreamInfo _Requested(
    int width,
    int height,
    int bitsPerPixel = 0,
    int index = 0,
    MediaStreamKind kind = MediaStreamKind.Video,
    string? codec = null) => new() {
    Index = index,
    Kind = kind,
    Codec = codec == null ? CodecTag.None : CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
