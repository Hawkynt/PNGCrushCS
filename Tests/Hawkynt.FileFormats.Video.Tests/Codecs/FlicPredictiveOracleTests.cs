using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.FlicVideo;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Puts FLIC's predicted pictures — not just its opening one — to ffmpeg, at every depth the
/// writer emits, and requires the samples back unchanged.
/// </summary>
/// <remarks>
/// The rest of the FLIC suite checks that a delta chunk of the expected type appears and that this
/// package's own decoder reproduces the picture from it. Neither says the delta is correct. Encoder
/// and decoder share a line grammar, a packet count convention, a skip rule and a byte order; a
/// mistake made consistently in both halves round-trips perfectly, and the one conformance test the
/// codec had handed ffmpeg a single 7x5 intra frame and asked only whether a picture of that size
/// came back. Every predicted picture the format exists for went unverified.
/// <para/>
/// A predicted codec also fails in a way one frame cannot show. A delta that is slightly wrong
/// leaves a canvas that is slightly wrong, which the next delta is applied to; the error
/// accumulates and is only ever corrected by a key frame. So these clips run twenty-four frames
/// with no key frame after the first, and every frame is compared, not the last.
/// <para/>
/// Each depth is asked for in its own layout — <c>rgb24</c> through FLIC's exact palette lookup for
/// 8-bit, <c>rgb565le</c>, <c>bgr24</c> and <c>rgb555le</c> for the DTA true-colour depths — so the
/// comparison is an equality on the coded samples rather than a tolerance after a conversion.
/// </remarks>
[TestFixture]
public sealed class FlicPredictiveOracleTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 32;
  private const int _FRAMES = 24;

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryPredictedIndexedPicture() {
    FFmpegOracle.RequireAvailable();

    var palette = _GreyRampPalette();
    var sources = _Clip(step => _IndexedPicture(step, palette));
    var (file, chunks) = _Encode(sources, 8);

    Assert.That(chunks.Skip(1).Any(frame => frame.Contains(FliChunkType.SS2)), Is.True,
      "no predicted picture was written: every frame fell back to a whole-frame BRUN.");

    // PAL8 to RGB24 is a lookup, not a conversion, so this comparison stays exact.
    var expected = sources.Select(picture => _ExpandPalette(picture, palette)).ToList();
    _AssertFfmpegAgrees(file, "rgb24", _WIDTH * _HEIGHT * 3, expected);
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryPredictedRgb565Picture() {
    FFmpegOracle.RequireAvailable();

    var sources = _Clip(step => _Rgb565Picture(step));
    var (file, chunks) = _Encode(sources, 16);

    Assert.That(chunks.Skip(1).Any(frame => frame.Contains(FliChunkType.DTA_LC)), Is.True,
      "no predicted picture was written: every frame fell back to a whole-frame DTA_BRUN.");

    _AssertFfmpegAgrees(file, "rgb565le", _WIDTH * _HEIGHT * 2, sources.Select(p => p.PixelData).ToList());
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryPredictedBgr24Picture() {
    FFmpegOracle.RequireAvailable();

    var sources = _Clip(_Bgr24Picture);
    var (file, chunks) = _Encode(sources, 24);

    Assert.That(chunks.Skip(1).Any(frame => frame.Contains(FliChunkType.DTA_LC)), Is.True,
      "no predicted picture was written: every frame fell back to a whole-frame DTA_BRUN.");

    _AssertFfmpegAgrees(file, "bgr24", _WIDTH * _HEIGHT * 3, sources.Select(p => p.PixelData).ToList());
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryPredictedFifteenBitPicture() {
    FFmpegOracle.RequireAvailable();

    var sources = _Clip(_Rgb555Picture);
    var (file, chunks) = _Encode(sources, 15);

    Assert.That(chunks.Skip(1).Any(frame => frame.Contains(FliChunkType.DTA_LC)), Is.True,
      "no predicted picture was written: every frame fell back to a whole-frame DTA_BRUN.");

    // The writer takes RGB24 it can represent exactly in five bits a channel and stores RGB555;
    // reducing the source back to five bits is what the bitstream actually holds.
    var expected = sources.Select(picture => _ToRgb555(picture.PixelData)).ToList();
    _AssertFfmpegAgrees(file, "rgb555le", _WIDTH * _HEIGHT * 2, expected);
  }

  [Test]
  [Category("Unit")]
  public void TheWholeFrameFallbackIsTakenWhenTheDeltaWouldBeLarger() {
    // The other direction of the same decision. Without this the encoder could be one that only
    // ever emits deltas, which on a picture that changes everywhere is the larger encoding, and
    // nothing in the suite would say so.
    var first = _Rgb565Picture(0);
    var second = _Rgb565Noise();
    var encoder = FlicVideoEncoder.Create(_Stream(16));

    Assert.That(encoder.TryEncode(first, 0, out var key), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var wholesale), Is.True);

    Assert.Multiple(() => {
      Assert.That(_ChunkTypes(key.Data.Span), Does.Contain(FliChunkType.DTA_BRUN));
      Assert.That(_ChunkTypes(wholesale.Data.Span), Does.Contain(FliChunkType.DTA_BRUN),
        "a picture that changed everywhere must not be written as a delta larger than the picture.");
      Assert.That(_ChunkTypes(wholesale.Data.Span), Does.Not.Contain(FliChunkType.DTA_LC));
      Assert.That(wholesale.IsKeyFrame, Is.True);
    });

    var decoder = FlicVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(key, out _), Is.True);
    Assert.That(decoder.TryDecode(wholesale, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(second.PixelData));
  }

  // ============================================================================================
  // Clip construction
  // ============================================================================================

  /// <summary>
  /// A static background with a small moving foreground, so most of each picture is unchanged.
  /// </summary>
  /// <remarks>
  /// A clip that changed everywhere would be encoded as whole frames and would prove nothing about
  /// prediction; one that changed nowhere would be all skips. A few moving rows is the shape that
  /// makes the delta the smaller encoding and therefore the one the writer picks.
  /// </remarks>
  private static List<RawImage> _Clip(Func<int, RawImage> picture)
    => Enumerable.Range(0, _FRAMES).Select(picture).ToList();

  private static (byte[] File, List<ushort[]> Chunks) _Encode(IReadOnlyList<RawImage> sources, int depth) {
    var encoder = FlicVideoEncoder.Create(_Stream(depth));
    var packets = new List<CodedPacket>(sources.Count);
    var chunks = new List<ushort[]>(sources.Count);

    foreach (var source in sources) {
      Assert.That(encoder.TryEncode(source, packets.Count, out var packet), Is.True);
      packets.Add(packet);
      chunks.Add(_ChunkTypes(packet.Data.Span));
    }

    return (VideoIO.Mux<FliWriter>([encoder.DescribeStream()], packets), chunks);
  }

  private static void _AssertFfmpegAgrees(
    byte[] file, string pixelFormat, int frameBytes, IReadOnlyList<byte[]> expected) {
    // The extension is deliberately the plain one: ffmpeg probes FLIC by its magic word, and a
    // stream it can only open because the name told it so is not a stream it has recognised.
    var path = Path.Combine(Path.GetTempPath(), $"flic-{Guid.NewGuid():N}.flc");

    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail, pictures) = FFmpegOracle.TryDecodePicturesAs(
        path, _WIDTH, _HEIGHT, expected.Count, pixelFormat, frameBytes);
      Assert.That(decoded, Is.True, detail);

      for (var frame = 0; frame < expected.Count; ++frame) {
        var wanted = expected[frame];
        var got = pictures.AsSpan(frame * frameBytes, frameBytes);
        for (var i = 0; i < frameBytes; ++i)
          if (got[i] != wanted[i])
            Assert.Fail(
              $"frame {frame}: ffmpeg decoded {pixelFormat} byte {i} as {got[i]} where {wanted[i]} was "
              + "encoded. A predicted picture has drifted from the canvas the writer thought it had.");
      }
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  // ============================================================================================
  // Pictures
  // ============================================================================================

  private static byte[] _GreyRampPalette() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 7 & 0xFF);
    }
    return palette;
  }

  private static RawImage _IndexedPicture(int step, byte[] palette) {
    var pixels = new byte[_WIDTH * _HEIGHT];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x)
      pixels[y * _WIDTH + x] = (byte)((x * 5 + y * 11) & 0xFF);

    _Stripe(step, (x, y) => pixels[y * _WIDTH + x] = (byte)(step * 9 + x & 0xFF));
    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  private static RawImage _Rgb565Picture(int step) {
    var pixels = new byte[_WIDTH * _HEIGHT * 2];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x)
      BinaryPrimitives.WriteUInt16LittleEndian(
        pixels.AsSpan((y * _WIDTH + x) * 2), (ushort)(0x0841 * ((x + y) & 31) + x));

    _Stripe(step, (x, y) => BinaryPrimitives.WriteUInt16LittleEndian(
      pixels.AsSpan((y * _WIDTH + x) * 2), (ushort)(0xF000 | (step * 37 + x) & 0x0FFF)));
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb565, PixelData = pixels };
  }

  private static RawImage _Rgb565Noise() {
    var pixels = new byte[_WIDTH * _HEIGHT * 2];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 181 + (i >> 5) * 97);
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb565, PixelData = pixels };
  }

  private static RawImage _Bgr24Picture(int step) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * 3;
      pixels[at] = (byte)(x * 3);
      pixels[at + 1] = (byte)(y * 7);
      pixels[at + 2] = (byte)((x ^ y) * 5);
    }

    _Stripe(step, (x, y) => {
      var at = (y * _WIDTH + x) * 3;
      pixels[at] = (byte)(step * 11);
      pixels[at + 1] = 200;
      pixels[at + 2] = (byte)(x * 4);
    });
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Bgr24, PixelData = pixels };
  }

  /// <summary>
  /// A picture whose every channel is exactly representable in five bits, which is the only input
  /// the 15-bit writer accepts.
  /// </summary>
  private static RawImage _Rgb555Picture(int step) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * 3;
      pixels[at] = _Exact(x);
      pixels[at + 1] = _Exact(y);
      pixels[at + 2] = _Exact(x ^ y);
    }

    _Stripe(step, (x, y) => {
      var at = (y * _WIDTH + x) * 3;
      pixels[at] = 255;
      pixels[at + 1] = _Exact(step);
      pixels[at + 2] = _Exact(x + step);
    });
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>The eight-bit value a five-bit sample widens to, so the trip back is exact.</summary>
  private static byte _Exact(int seed) {
    var five = seed & 31;
    return (byte)((five << 3) | (five >> 2));
  }

  /// <summary>Rewrites the small rectangle that moves, and nothing else.</summary>
  /// <remarks>
  /// A rectangle, not a full-width band. A delta line whose change starts at column zero and runs
  /// to the last column never exercises the per-packet column skip, and the skip is where a writer
  /// that counted bytes where the format counts pixels would go wrong. Moving it diagonally leaves
  /// both a leading and a trailing unchanged run on every line the delta touches.
  /// </remarks>
  private static void _Stripe(int step, Action<int, int> write) {
    var top = step * 2 % (_HEIGHT - 4);
    var left = step * 3 % (_WIDTH - 20) + 3;
    for (var y = top; y < top + 4; ++y)
    for (var x = left; x < left + 16; ++x)
      write(x, y);
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _ExpandPalette(RawImage picture, byte[] palette) {
    var result = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i) {
      var entry = picture.PixelData[i] * 3;
      result[i * 3] = palette[entry];
      result[i * 3 + 1] = palette[entry + 1];
      result[i * 3 + 2] = palette[entry + 2];
    }
    return result;
  }

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

  private static MediaStreamInfo _Stream(int depth) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("FLIC"),
    Handler = CodecTag.FromCharacters("FLIC"),
    Width = _WIDTH,
    Height = _HEIGHT,
    BitsPerPixel = depth,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static ushort[] _ChunkTypes(ReadOnlySpan<byte> data) {
    var result = new List<ushort>();
    for (var at = 0; at < data.Length;) {
      var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]));
      result.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]));
      at += size;
    }
    return result.ToArray();
  }
}
