using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Indeo.Tests;

[TestFixture]
[Category("Conformance")]
public sealed class Indeo5FfmpegOracleTests {

  [Test]
  public void FfmpegDecodesTheCompleteIPSequence() {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("IV50"),
      Handler = CodecTag.FromCharacters("IV50"),
      Width = width,
      Height = height,
      TimeBase = new(1, 25),
      FrameRate = new(25, 1),
    };
    var encoder = Indeo5VideoEncoder.Create(requested);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 3; ++frame) {
      Assert.That(encoder.TryEncode(_Picture(width, height, frame), frame, out var packet), Is.True);
      packets.Add(packet);
    }

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail) = FFmpegOracle.TryDecodeFrameCount(path, width, height, expectedFrames: 3);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static RawImage _Picture(int width, int height, int frame) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x * 5 + y * 3 + frame * 41) & 0xFF);
        pixels[at + 1] = (byte)((x * 2 + y * 7 + frame * 29) & 0xFF);
        pixels[at + 2] = (byte)(((x + frame * 3) / 8 + y / 8) % 2 == 0 ? 240 : 16);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
