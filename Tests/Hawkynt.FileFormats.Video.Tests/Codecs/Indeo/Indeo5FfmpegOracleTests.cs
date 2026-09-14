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
    var encoder = Indeo5VideoEncoder.Create(_Stream(64, 48));
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 3; ++frame) {
      Assert.That(encoder.TryEncode(_Picture(64, 48, frame), frame, out var packet), Is.True);
      packets.Add(packet);
    }

    _AssertFfmpegDecodes(encoder, packets, expectedFrames: 3);
  }

  [Test]
  public void FfmpegDecodesANoReferenceDroppablePictureBetweenReferences() {
    var encoder = Indeo5VideoEncoder.Create(_Stream(64, 48));
    var packets = new List<CodedPacket>();

    Assert.That(encoder.TryEncode(_Picture(64, 48, 0), 0, out var first), Is.True);
    packets.Add(first);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 1), 1, Indeo5FrameMode.Disposable, out var disposable), Is.True);
    packets.Add(disposable);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 2), 2, Indeo5FrameMode.Reference, out var following), Is.True);
    packets.Add(following);

    _AssertFfmpegDecodes(encoder, packets, expectedFrames: 3);
  }

  [Test]
  public void FfmpegDecodesTheScalableDroppableChain() {
    var encoder = Indeo5VideoEncoder.Create(_Stream(64, 48), scalable: true);
    var packets = new List<CodedPacket>();

    Assert.That(encoder.TryEncode(_Picture(64, 48, 0), 0, out var first), Is.True);
    packets.Add(first);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 1), 1, Indeo5FrameMode.ScalableDisposable, out var scalableA), Is.True);
    packets.Add(scalableA);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 2), 2, Indeo5FrameMode.ScalableDisposable, out var scalableB), Is.True);
    packets.Add(scalableB);
    Assert.That(encoder.TryEncode(_Picture(64, 48, 3), 3, Indeo5FrameMode.Reference, out var following), Is.True);
    packets.Add(following);

    _AssertFfmpegDecodes(encoder, packets, expectedFrames: 4);
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IV50"),
    Handler = CodecTag.FromCharacters("IV50"),
    Width = width,
    Height = height,
    TimeBase = new(1, 25),
    FrameRate = new(25, 1),
  };

  private static void _AssertFfmpegDecodes(
    Indeo5VideoEncoder encoder,
    IReadOnlyList<CodedPacket> packets,
    int expectedFrames) {

    FFmpegOracle.RequireAvailable();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail) = FFmpegOracle.TryDecodeFrameCount(path, 64, 48, expectedFrames);
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
