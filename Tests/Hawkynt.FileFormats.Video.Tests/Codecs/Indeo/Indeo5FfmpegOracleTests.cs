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

  /// <summary>
  /// Hands the muxed clip to FFmpeg, asks for the YVU9 samples the codec actually carries, and
  /// requires them to be the same bytes this package's own decoder produces.
  /// </summary>
  /// <remarks>
  /// FFmpeg is asked for <c>yuv410p</c> because that is Indeo 5's own layout, so nothing converts
  /// colour on either side and a single sample of difference is a real disagreement about the
  /// bitstream rather than a rounding difference between two YUV-to-RGB matrices.
  /// <para/>
  /// The frame count is still checked, because the byte length has to match before the samples can
  /// be compared at all, but it is no longer the whole claim. A count cannot see a coefficient in
  /// the wrong band or a prediction from the wrong buffer, and those are exactly the mistakes an
  /// encoder written against this repository's own decoder makes without either half noticing.
  /// </remarks>
  private static void _AssertFfmpegDecodes(
    Indeo5VideoEncoder encoder,
    IReadOnlyList<CodedPacket> packets,
    int expectedFrames) {

    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    var chromaWidth = (width + 3) >> 2;
    var chromaHeight = (height + 3) >> 2;
    var lumaBytes = width * height;
    var chromaBytes = chromaWidth * chromaHeight;
    var frameBytes = lumaBytes + 2 * chromaBytes;

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    bool decoded;
    string detail;
    byte[] samples;
    try {
      File.WriteAllBytes(path, avi);
      (decoded, detail, samples) =
        FFmpegOracle.TryDecodePicturesAs(path, width, height, expectedFrames, "yuv410p", frameBytes);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(decoded, Is.True, detail);

    var ours = new Indeo5Decoder(width, height);
    for (var frame = 0; frame < expectedFrames; ++frame) {
      var picture = ours.Decode(packets[frame].Data);
      Assert.That(picture, Is.Not.Null, $"our decoder produced no picture for frame {frame}");

      var at = frame * frameBytes;
      _AssertPlane(samples, at, picture!.Luma, frame, "luma");
      _AssertPlane(samples, at + lumaBytes, picture.ChromaBlue, frame, "blue chroma");
      _AssertPlane(samples, at + lumaBytes + chromaBytes, picture.ChromaRed, frame, "red chroma");
    }
  }

  private static void _AssertPlane(byte[] reference, int at, byte[] ours, int frame, string plane) {
    for (var i = 0; i < ours.Length; ++i)
      if (reference[at + i] != ours[i])
        Assert.Fail(
          $"the {plane} plane of frame {frame} differs from FFmpeg's at sample {i}: "
          + $"FFmpeg decoded {reference[at + i]} and this package decoded {ours[i]}");
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
