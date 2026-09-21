using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.ZeroCodec.Tests;

[TestFixture]
public class ZeroCodecVideoEncoderTests {

  private static readonly CodecTag _Zeco = CodecTag.FromCharacters("ZECO");

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    MediaStreamKind kind = MediaStreamKind.Video,
    int index = 0) => new() {
    Index = index,
    Kind = kind,
    Codec = _Zeco,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Frame(int width, int height, params byte[] planes) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Yuv422P8,
    PixelData = planes,
  };

  private static byte[] _Inflate(ReadOnlyMemory<byte> data, int count) {
    using var source = new MemoryStream(data.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    var result = new byte[count];
    zlib.ReadExactly(result);
    Assert.That(zlib.ReadByte(), Is.EqualTo(-1));
    return result;
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheZeroCodecEncoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(8, 3));

    Assert.That(encoder, Is.TypeOf<ZeroCodecVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var described = ZeroCodecVideoEncoder.Create(_Stream(8, 3, index: 2)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(ZeroCodecVideoEncoder.Codec, Is.EqualTo(_Zeco));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Codec, Is.EqualTo(_Zeco));
      Assert.That(described.Handler, Is.EqualTo(_Zeco));
      Assert.That(described.Width, Is.EqualTo(8));
      Assert.That(described.Height, Is.EqualTo(3));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(ZeroCodecVideoDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FirstPictureIsALiteralBottomUpKeyFrame() {
    var frame = _Frame(
      2,
      2,
      // Y plane, top row then bottom row.
      16, 17, 235, 234,
      // Cb plane.
      128, 129,
      // Cr plane.
      130, 131);
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 2));

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(_Inflate(packet.Data, 8), Is.EqualTo(new byte[] {
        // Bottom row first.
        129, 235, 131, 234,
        128, 16, 130, 17,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnchangedPictureBecomesAnAllZeroPFrame() {
    var frame = _Frame(2, 1, 16, 17, 128, 130);
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(_Inflate(second.Data, 4), Is.EqualTo(new byte[4]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChangedNonzeroBytesAreLiteralInAPFrame() {
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));
    var first = _Frame(2, 1, 16, 16, 128, 128);
    var second = _Frame(2, 1, 235, 16, 128, 128);

    Assert.That(encoder.TryEncode(first, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.False);
      Assert.That(_Inflate(packet.Data, 4), Is.EqualTo(new byte[] { 0, 235, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AChangeToZeroForcesAKeyFrameBecauseZeroIsThePredictorSentinel() {
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(2, 1));
    var first = _Frame(2, 1, 16, 16, 128, 128);
    var second = _Frame(2, 1, 0, 16, 128, 128);

    Assert.That(encoder.TryEncode(first, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(_Inflate(packet.Data, 4), Is.EqualTo(new byte[] { 128, 0, 128, 16 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void IAndPFramesRoundTripTheirYuvSamplesExactly() {
    var pictures = new[] {
      _Frame(4, 2, 16, 17, 18, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
      _Frame(4, 2, 16, 17, 80, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
      _Frame(4, 2, 16, 17, 0, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135),
    };
    var encoder = ZeroCodecVideoEncoder.Create(_Stream(4, 2));
    var decoder = ZeroCodecVideoDecoder.Create(encoder.DescribeStream());

    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"picture {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AviPreservesTheKeyFlagNeededToDistinguishIAndPFrames() {
    var stream = _Stream(4, 2);
    var encoder = ZeroCodecVideoEncoder.Create(stream);
    var frame = _Frame(4, 2, 16, 17, 18, 19, 20, 21, 22, 23, 128, 129, 130, 131, 132, 133, 134, 135);
    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [first, second]);
    var container = AviContainer.FromBytes(avi);
    var demuxedStream = AviContainer.Streams(container).Single();
    var packets = AviContainer.ReadPackets(container).ToArray();

    Assert.Multiple(() => {
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(packets[0].IsKeyFrame, Is.True);
      Assert.That(packets[1].IsKeyFrame, Is.False);
    });

    var decoder = ZeroCodecVideoDecoder.Create(demuxedStream);
    Assert.That(decoder.TryDecode(packets[0], out _), Is.True);
    Assert.That(decoder.TryDecode(packets[1], out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  // ============================================================================================
  // The external oracle
  // ============================================================================================

  /// <summary>How many pictures the oracle clip carries, so two of them are inter-coded.</summary>
  private const int _ORACLE_FRAMES = 3;

  private const int _ORACLE_WIDTH = 64;
  private const int _ORACLE_HEIGHT = 48;

  /// <summary>How many rows of the picture each frame's moving band covers.</summary>
  private const int _ORACLE_BAND_HEIGHT = 8;

  /// <summary>
  /// A fixed background with one band of rows displaced, and the band in a different place in every
  /// frame.
  /// </summary>
  /// <remarks>
  /// The shape of the sequence is load-bearing twice over, and both traps were walked into before
  /// they were written down.
  /// <para/>
  /// <b>Nothing is ever zero.</b> Zero is the inter picture's "unchanged" sentinel, so a sample that
  /// falls to zero forces the encoder to promote the whole picture to an I picture. A clip that
  /// promoted every picture would run this oracle over three intra frames and report the inter path
  /// working without one ever having been coded. Every sample here stays inside 1..254.
  /// <para/>
  /// <b>The band moves rather than grows.</b> A sequence whose changed region only ever grows makes
  /// the region each frame left alone identical in every frame before it, and then it does not
  /// matter which earlier picture the predictor differenced against — every wrong reference happens
  /// to hold the right bytes and the check passes while the reference is broken. Measured, not
  /// assumed: an earlier version of this generator grew the region, and deliberately freezing the
  /// encoder's reference at the opening picture did not fail a single test. Moving the band leaves
  /// rows that changed in frame two and changed back in frame three, so a predictor that differenced
  /// against the wrong picture writes "unchanged" over them and FFmpeg reconstructs the band from
  /// the frame before, which is not the picture that went in.
  /// </remarks>
  private static RawImage _OraclePicture(int frame) {
    var width = _ORACLE_WIDTH;
    var height = _ORACLE_HEIGHT;
    var chromaWidth = width / 2;
    var luma = new byte[width * height];
    var cb = new byte[chromaWidth * height];
    var cr = new byte[chromaWidth * height];
    var bandStart = frame * _ORACLE_BAND_HEIGHT;
    var bandEnd = bandStart + _ORACLE_BAND_HEIGHT;

    for (var y = 0; y < height; ++y) {
      var displaced = y >= bandStart && y < bandEnd;

      for (var x = 0; x < width; ++x)
        luma[y * width + x] = (byte)(1 + (x * 3 + y * 5 + (displaced ? 101 : 0)) % 254);

      for (var x = 0; x < chromaWidth; ++x) {
        cb[y * chromaWidth + x] = (byte)(1 + (x * 7 + y + (displaced ? 61 : 0)) % 254);
        cr[y * chromaWidth + x] = (byte)(1 + (x + y * 11 + (displaced ? 23 : 0)) % 254);
      }
    }

    var pixels = new byte[luma.Length + cb.Length + cr.Length];
    luma.CopyTo(pixels, 0);
    cb.CopyTo(pixels, luma.Length);
    cr.CopyTo(pixels, luma.Length + cb.Length);

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = pixels };
  }

  /// <summary>
  /// The packed UYVY bytes a picture's planes stand for, worked out here rather than asked of the
  /// code under test.
  /// </summary>
  /// <remarks>
  /// Calling the encoder's own packing helper to build the expected answer would compare the helper
  /// with itself: a helper that packed VYUY would produce matching "expected" bytes and the check
  /// would pass while every colour was wrong. The interleave is four lines of arithmetic, so it is
  /// written out.
  /// </remarks>
  private static byte[] _ExpectedUyvy(RawImage picture) {
    var width = picture.Width;
    var height = picture.Height;
    var chromaWidth = width / 2;
    var source = picture.PixelData.AsSpan();
    var luma = source[..(width * height)];
    var cb = source.Slice(width * height, chromaWidth * height);
    var cr = source.Slice(width * height + chromaWidth * height, chromaWidth * height);
    var result = new byte[width * height * 2];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < chromaWidth; ++x) {
      var at = (y * chromaWidth + x) * 4;
      result[at] = cb[y * chromaWidth + x];
      result[at + 1] = luma[y * width + x * 2];
      result[at + 2] = cr[y * chromaWidth + x];
      result[at + 3] = luma[y * width + x * 2 + 1];
    }

    return result;
  }

  /// <summary>
  /// Hands FFmpeg a multi-picture ZeroCodec AVI this package wrote and requires every sample back.
  /// </summary>
  /// <remarks>
  /// The encoder and the decoder here were written from one reading of the format, so they agree
  /// with each other whether that reading is right or wrong, and the round-trip checks above cannot
  /// tell those two cases apart. FFmpeg's ZeroCodec decoder was written from a different reading by
  /// different people, so what it hands back is evidence.
  /// <para/>
  /// Three things are proved that a round trip cannot prove on its own. The clip is decoded whole
  /// rather than to its opening picture, so the two inter pictures are reached; their content
  /// differs from the picture before them, so a predictor that ignored its reference would produce
  /// the wrong samples rather than the right ones by luck; and the comparison is in UYVY, the
  /// codec's own sample layout, so FFmpeg applies no colour conversion and the check is exact rather
  /// than approximate. A wrong row order, a wrong macropixel order, a P picture that silently
  /// re-encoded as intra, or an AVI index whose key flags land on the wrong picture all fail it.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void FFmpegReadsBackEveryPictureOfAZeroCodecAviExactly() {
    FFmpegOracle.RequireAvailable();

    var encoder = ZeroCodecVideoEncoder.Create(_Stream(_ORACLE_WIDTH, _ORACLE_HEIGHT));
    var pictures = new RawImage[_ORACLE_FRAMES];
    var packets = new CodedPacket[_ORACLE_FRAMES];

    for (var i = 0; i < _ORACLE_FRAMES; ++i) {
      pictures[i] = _OraclePicture(i);
      Assert.That(encoder.TryEncode(pictures[i], i, out packets[i]), Is.True, $"picture {i} was not encoded");
    }

    // Without this the oracle below could pass over three intra pictures and prove nothing about the
    // inter path it exists to check.
    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True, "the opening picture has to be an I picture");
      for (var i = 1; i < _ORACLE_FRAMES; ++i)
        Assert.That(packets[i].IsKeyFrame, Is.False, $"picture {i} was not coded as a P picture");
    });

    var directory = Directory.CreateTempSubdirectory("zerocodecoracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.avi");
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));

      var frameBytes = _ORACLE_WIDTH * _ORACLE_HEIGHT * 2;
      var (decoded, detail, samples) = FFmpegOracle.TryDecodePicturesAs(
        path, _ORACLE_WIDTH, _ORACLE_HEIGHT, _ORACLE_FRAMES, "uyvy422", frameBytes);

      Assert.That(decoded, Is.True, $"ffmpeg did not read the ZeroCodec clip back: {detail}");

      for (var i = 0; i < _ORACLE_FRAMES; ++i)
        Assert.That(
          samples.AsSpan(i * frameBytes, frameBytes).ToArray(),
          Is.EqualTo(_ExpectedUyvy(pictures[i])),
          $"ffmpeg decoded picture {i} to samples other than the ones encoded");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void RefusesUnsupportedStreamsAndSourceFrames() {
    Assert.Throws<NotSupportedException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(4, 1, MediaStreamKind.Audio)));
    Assert.Throws<InvalidDataException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(0, 1)));
    Assert.Throws<NotSupportedException>(
      () => ZeroCodecVideoEncoder.Create(_Stream(3, 1)));

    var encoder = ZeroCodecVideoEncoder.Create(_Stream(4, 1));
    Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Frame(4, 2, new byte[16]), 0, out _));
    Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Frame(4, 1, new byte[7]), 0, out _));
    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }
}
