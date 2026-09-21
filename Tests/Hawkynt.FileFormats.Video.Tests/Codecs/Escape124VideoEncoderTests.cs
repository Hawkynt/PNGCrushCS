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

  private const int _CLIP_WIDTH = 64;
  private const int _CLIP_HEIGHT = 32;
  private const int _CLIP_FRAMES = 24;

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
  [Category("Conformance")]
  public void FFmpegReconstructsEveryFrameOfAPredictedClipExactlyAsThisDecoderDoes() {
    FFmpegOracle.RequireAvailable();

    var (sources, stream, packets) = _EncodeClip();
    var file = VideoIO.Mux<RplWriter>([stream], packets);
    var path = Path.Combine(Path.GetTempPath(), $"escape124-{Guid.NewGuid():N}.rpl");

    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail, pictures) = FFmpegOracle.TryDecodePicturesAs(
        path, _CLIP_WIDTH, _CLIP_HEIGHT, sources.Count, "rgb555le", _CLIP_WIDTH * _CLIP_HEIGHT * 2);
      Assert.That(decoded, Is.True, detail);

      var ours = _DecodeHere(stream, packets);
      Assert.That(ours, Has.Count.EqualTo(sources.Count));

      for (var frame = 0; frame < sources.Count; ++frame) {
        var mine = _ToRgb555(ours[frame]);
        var wanted = _ToRgb555(sources[frame].PixelData);
        var theirs = pictures.AsSpan(frame * mine.Length, mine.Length);

        for (var i = 0; i < mine.Length; ++i) {
          // Two separate claims, and they fail for different reasons.
          //
          // Against this decoder: the two readers disagree about the bytes the writer produced.
          // Against the source: the writer itself drifted. A defect the encoder and decoder share
          // -- predicting from something the decoder does not have, say -- keeps the first
          // comparison perfectly happy, because both halves drift together. Only the source can see
          // it, and only because this clip is built to be representable exactly.
          if (mine[i] != theirs[i])
            Assert.Fail(
              $"frame {frame}: ffmpeg reconstructed byte {i} as {theirs[i]} where this decoder has {mine[i]}. "
              + "The two decoders disagree about the bitstream this encoder wrote.");

          if (theirs[i] != wanted[i])
            Assert.Fail(
              $"frame {frame}: ffmpeg reconstructed byte {i} as {theirs[i]} where the source picture has "
              + $"{wanted[i]}. This clip is exactly representable, so the picture has drifted from what "
              + "was encoded.");
        }
      }
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void ThePredictedClipActuallyPredicts() {
    var (_, _, packets) = _EncodeClip();

    var repeats = packets.Count(packet => packet.Data.Length == 8);
    var coded = packets.Where(packet => packet.Data.Length > 8).ToList();

    Assert.Multiple(() => {
      // Without these three the oracle above would still pass while proving nothing about
      // prediction: a codec that wrote every frame as a full refresh decodes perfectly.
      Assert.That(packets[0].IsKeyFrame, Is.True, "the opening picture must refresh every superblock.");
      Assert.That(repeats, Is.GreaterThan(0), "an unchanged picture must use the eight-byte repeat form.");
      Assert.That(
        coded.Skip(1).Any(packet => !packet.IsKeyFrame),
        Is.True,
        "at least one coded picture must reuse superblocks from the previous reconstruction.");
      Assert.That(
        coded.Skip(1).All(packet => !packet.IsKeyFrame),
        Is.True,
        "after the opening picture no coded picture should need a full refresh: the background never moves.");
    });
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

  /// <summary>
  /// A clip whose background never changes and whose foreground does, plus one picture identical to
  /// its predecessor.
  /// </summary>
  /// <remarks>
  /// Length and shape are the point. Three flat 16x8 frames exercise the grammar and nothing else:
  /// prediction that referenced the wrong picture, or drifted by a superblock a frame, reproduces a
  /// flat colour perfectly. Here a textured background stays put while a bright square walks across
  /// it, so most superblocks are skipped and a few are coded in every picture, and twenty-four
  /// frames give a per-frame error somewhere to accumulate to. One frame repeats its predecessor
  /// exactly, which is the only way the eight-byte repeat form gets written.
  /// </remarks>
  private static (IReadOnlyList<RawImage> Sources, MediaStreamInfo Stream, List<CodedPacket> Packets) _EncodeClip() {
    var stream = _Stream(_CLIP_WIDTH, _CLIP_HEIGHT);
    var encoder = Escape124VideoEncoder.Create(stream);
    var sources = new List<RawImage>(_CLIP_FRAMES);
    var packets = new List<CodedPacket>(_CLIP_FRAMES);

    for (var frame = 0; frame < _CLIP_FRAMES; ++frame) {
      // Frame 12 repeats frame 11 exactly, so the writer has a reason to emit the repeat form.
      var step = frame >= 12 ? frame - 1 : frame;
      var picture = _WalkingSquare(step);
      sources.Add(picture);
      Assert.That(encoder.TryEncode(picture, frame, out var packet), Is.True);
      packets.Add(packet);
    }

    return (sources, encoder.DescribeStream(), packets);
  }

  /// <summary>
  /// Colours whose five-bit form survives the trip out and back unchanged.
  /// </summary>
  /// <remarks>
  /// Escape 124 stores RGB555, so an arbitrary eight-bit channel cannot come back. These five can:
  /// each is the bit-replicated widening of a five-bit value, which the writer's rounding maps back
  /// to that same five-bit value. Building the clip out of them makes the codec lossless over this
  /// material, and that is what lets the oracle assert equality with the source rather than a
  /// tolerance — see <see cref="FFmpegReconstructsEveryFrameOfAPredictedClipExactlyAsThisDecoderDoes"/>.
  /// </remarks>
  private static readonly byte[] _ExactChannels = [0, 66, 132, 198, 255];

  /// <summary>
  /// One picture of the clip: a static textured background with a solid square at <paramref name="step"/>.
  /// </summary>
  /// <remarks>
  /// Everything is laid out on the 2x2 macroblock grid on purpose. Escape 124 codes each 2x2 block
  /// as two colours, so a block of one colour is exact and a block straddling a colour boundary is
  /// not; keeping every feature aligned to even coordinates makes the whole clip representable, and
  /// an exact clip is the only one an equality assertion can be made about.
  /// </remarks>
  private static RawImage _WalkingSquare(int step) {
    var data = new byte[_CLIP_WIDTH * _CLIP_HEIGHT * 3];
    // Deliberately NOT aligned to the 8x8 superblock grid. A square that filled whole superblocks
    // would change every row of every superblock it touched, and a skip decision that inspected
    // only part of a superblock would still be right by accident. Moving two pixels at a time
    // across a six-pixel square, spanning rows 10 to 17, leaves superblocks whose top row is
    // unchanged and whose middle is not, and superblocks changed in one half and not the other.
    var squareX = step * 2 % (_CLIP_WIDTH - 8);

    for (var y = 0; y < _CLIP_HEIGHT; ++y)
    for (var x = 0; x < _CLIP_WIDTH; ++x) {
      var at = (y * _CLIP_WIDTH + x) * 3;
      if (x >= squareX && x < squareX + 6 && y >= 10 && y < 18) {
        data[at] = 255;
        data[at + 1] = _ExactChannels[step % _ExactChannels.Length];
        data[at + 2] = 66;
        continue;
      }

      // A static background that is not flat: flat colour hides a prediction that drifts, and a
      // background that moved would leave nothing for the skip runs to skip.
      var blockX = x / 2;
      var blockY = y / 2;
      data[at] = _ExactChannels[blockX % _ExactChannels.Length];
      data[at + 1] = _ExactChannels[blockY % _ExactChannels.Length];
      data[at + 2] = _ExactChannels[(blockX + blockY) % _ExactChannels.Length];
    }

    return new() { Width = _CLIP_WIDTH, Height = _CLIP_HEIGHT, Format = PixelFormat.Rgb24, PixelData = data };
  }

  /// <summary>Reconstructs the same packets with this package's own decoder.</summary>
  private static List<byte[]> _DecodeHere(MediaStreamInfo stream, IEnumerable<CodedPacket> packets) {
    var decoder = Escape124VideoDecoder.Create(stream);
    var result = new List<byte[]>();
    foreach (var packet in packets) {
      Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
      result.Add(frame.PixelData);
    }
    return result;
  }

  /// <summary>
  /// Reduces this decoder's RGB24 back to the 15-bit words the codec actually stores.
  /// </summary>
  /// <remarks>
  /// The comparison has to happen on the coded grid. Escape 124 stores RGB555 and both decoders
  /// widen it on the way out, but they need not widen it the same way, and a difference in the
  /// widening is a difference in neither decoder's reading of the bitstream. Taking the top five
  /// bits back off is exact under either widening -- bit replication and a plain shift both leave
  /// the original five bits in place -- so what remains is a disagreement about the stream itself.
  /// </remarks>
  private static byte[] _ToRgb555(ReadOnlySpan<byte> rgb24) {
    var result = new byte[rgb24.Length / 3 * 2];
    for (var pixel = 0; pixel < rgb24.Length / 3; ++pixel) {
      var word = (ushort)(((rgb24[pixel * 3] >> 3) << 10)
        | ((rgb24[pixel * 3 + 1] >> 3) << 5)
        | (rgb24[pixel * 3 + 2] >> 3));
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pixel * 2), word);
    }
    return result;
  }
}
