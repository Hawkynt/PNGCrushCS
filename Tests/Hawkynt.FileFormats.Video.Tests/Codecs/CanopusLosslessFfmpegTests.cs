using System;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class CanopusLosslessFfmpegTests {

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsNativeYuv422WrittenHere() {
    FFmpegOracle.RequireAvailable();

    const int width = 8;
    const int height = 4;
    var lumaLength = width * height;
    var chromaLength = width / 2 * height;
    var samples = new byte[lumaLength + 2 * chromaLength];
    for (var i = 0; i < lumaLength; ++i)
      samples[i] = (byte)(16 + i * 219 / Math.Max(1, lumaLength - 1));
    for (var i = 0; i < chromaLength; ++i) {
      samples[lumaLength + i] = (byte)(16 + (i * 37) % 225);
      samples[lumaLength + chromaLength + i] = (byte)(240 - (i * 53) % 225);
    }

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(width, height, 16));
    Assert.That(encoder.TryEncode(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = samples,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    }, 0, out var packet), Is.True);

    _AssertFfmpegReads(encoder.DescribeStream(), packet, width, height);
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsArgbWrittenHere() {
    FFmpegOracle.RequireAvailable();

    const int width = 8;
    const int height = 4;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      pixels[at] = (byte)((x + y) % 5 == 0 ? 0 : 32 + (x * 29 + y * 47) % 224);
      if (pixels[at] == 0)
        continue;
      pixels[at + 1] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 2] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 3] = (byte)((x * 43 + y * 71) & 0xFF);
    }

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(width, height, 32));
    Assert.That(encoder.TryEncode(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Argb32,
      PixelData = pixels,
    }, 0, out var packet), Is.True);

    _AssertFfmpegReads(encoder.DescribeStream(), packet, width, height);
  }

  private static void _AssertFfmpegReads(MediaStreamInfo stream, CodedPacket packet, int width, int height) {
    var avi = VideoIO.Mux<AviWriter>([stream], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, width, height);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CLLC"),
    Handler = CodecTag.FromCharacters("CLLC"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
