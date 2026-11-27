using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Fmvc;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>FM Screen Capture both ways, with FFmpeg as the oracle.</summary>
[TestFixture]
public sealed class FmvcVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void FmvcIsDecodedAndEncoded() {
    var stream = _Stream(64, 64);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<FmvcVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<FmvcVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void BlocksAreLaidOutAsFFmpegLaysThemOut() {
    // 200 pixels of 24 bits are 150 words: one block of 84 and a last of 66, which is 37 or more and so stands alone.
    var wide = new FmvcFrame(200, 300, 24);
    // 100 pixels are 75 words, less than 84, so the one block takes them all; 130 rows leave 18 for the last, under 49, so they join it.
    var narrow = new FmvcFrame(100, 130, 24);
    Assert.Multiple(() => {
      Assert.That((wide.BlocksWide, wide.BlocksHigh), Is.EqualTo((2, 3)));
      Assert.That(wide.Blocks[^1], Is.EqualTo((66, 76)));
      Assert.That((narrow.BlocksWide, narrow.BlocksHigh), Is.EqualTo((1, 1)));
      Assert.That(narrow.Blocks[0], Is.EqualTo((75, 130)));
    });
  }

  [TestCase(96, 64)]
  [TestCase(250, 170)]
  [TestCase(49, 49)]
  [Category("Unit")]
  public void APictureComesBackExactly(int width, int height) {
    var (_, ours, originals, _) = _Encode(width, height, 6);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void APictureTooSmallForABlockIsRefused()
    => Assert.That(() => FmvcVideoEncoder.Create(_Stream(48, 48)), Throws.InstanceOf<NotSupportedException>());

  [Test]
  [Category("Unit")]
  public void AnUnchangedFrameIsEightBytes() {
    var (_, _, _, packets) = _Encode(96, 64, 3, still: true);
    Assert.That(packets.Skip(1).Select(static p => p.Data.Length), Is.All.EqualTo(8));
  }

  [TestCase(96, 64)]
  [TestCase(250, 170)]
  [TestCase(49, 49)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _, _) = _Encode(width, height, 6);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "bgr24");
      Assert.That(decoded, Is.True, detail);
      var size = width * height * 3;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>FMVC_SAMPLES</c> at real films, such as FATE's <c>fmvc/</c> and <c>samples.ffmpeg.org/V-codecs/FMVC</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real FM Screen Capture films; point FMVC_SAMPLES at them.")]
  public void EveryPictureOfARealFmvcFilmMatchesFFmpeg()
    => RealSampleComparison.Run("FMVC_SAMPLES", FmvcVideoDecoder.Accepts, "bgr24");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("FMVC"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 10),
    FrameRate = new Rational(10, 1),
  };

  /// <summary>A screen: flat areas, text-like noise, and a window that moves; returns the film, this decoder's BGR, the originals as BGR, and the packets.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals, List<CodedPacket> Packets) _Encode(int width, int height, int count, bool still = false) {
    var encoder = FmvcVideoEncoder.Create(_Stream(width, height));
    var stream = encoder.DescribeStream();
    var decoder = FmvcVideoDecoder.Create(stream);
    var random = new Random(9);
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < count; ++n) {
      var step = still ? 0 : n;
      var bgr = new byte[width * height * 3];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = (y * width + x) * 3;
          var inWindow = x >= step * 5 && x < step * 5 + width / 3 && y >= height / 4 && y < height * 3 / 4;
          bgr[at] = (byte)(inWindow ? 200 : y < 8 ? 40 : 230);
          bgr[at + 1] = (byte)(inWindow ? ((x * 7 + y * 3) & 0x80) : 230);
          bgr[at + 2] = (byte)(inWindow ? random.Next(2) * 255 : 230);
        }
      if (still)
        random = new Random(9);
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = bgr };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData.ToArray());
      originals.Add(bgr);
    }
    return (VideoIO.Mux<AviWriter>([stream], packets), ours, originals, packets);
  }
}
