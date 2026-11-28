using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.SheerVideo;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Every SheerVideo layout, as frames of random bits read here and by FFmpeg: no real file has been found,
/// and any bits are a valid frame, so FFmpeg is held to every row decoder this way.
/// </summary>
[TestFixture]
[Category("Conformance")]
public sealed class SheerVideoCraftedFrameTests {

  private static IEnumerable<TestCaseData> Layouts() {
    foreach (var format in SheerDecode.Formats)
      foreach (var (width, height) in new[] { (40, 12), (37, 9) })
        yield return new TestCaseData(format.Tag, width, height).SetName($"{{m}}(0x{format.Tag:X8} {format.Decoder}, {width}x{height})");
  }

  [TestCaseSource(nameof(Layouts))]
  public void ACraftedFrameDecodesAsFFmpegDecodesIt(uint tag, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var format = SheerFrame.Find(tag)!.Value;
    var packet = new byte[SheerFrame.HeaderLength + width * height * 6];
    new Random((int)tag ^ width).NextBytes(packet.AsSpan(SheerFrame.HeaderLength));
    "Shir"u8.CopyTo(packet);
    BitConverter.TryWriteBytes(packet.AsSpan(16), tag);
    var decoded = SheerFrame.Decode(packet, width, height);

    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("Shr0"), Width = width, Height = height, BitsPerPixel = 24,
      TimeBase = new Rational(1, 25), FrameRate = new Rational(25, 1),
    };
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([stream], [new CodedPacket(0, packet, PresentationTimestamp: 0, IsKeyFrame: true)]));
      var native = format.PixelFormat.ToLowerInvariant() + (format.PixelFormat.EndsWith("10") ? "le" : "");
      var (ok, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, native);
      Assert.That(ok, Is.True, detail);
      var ours = _Native(decoded, width, height);
      // RGB0's fourth byte is never written by the decoder, so only the colour is compared.
      if (format.PixelFormat == "RGB0")
        for (var i = 3; i < ours.Length; i += 4)
          samples[i] = ours[i] = 0;
      Assert.That(samples, Is.EqualTo(ours));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>The planes as FFmpeg writes its native pixel format raw: packed four bytes a pixel, or each plane in turn.</summary>
  private static byte[] _Native(SheerFrame frame, int width, int height) {
    var bytes = new List<byte>();
    var (count, packed) = SheerFrame.Shape(frame.Format.PixelFormat);
    var ten = frame.Format.PixelFormat.EndsWith("10");
    var half = frame.Format.PixelFormat.Contains("422");
    for (var p = 0; p < count; ++p) {
      var w = packed ? 4 * width : p is 1 or 2 && half ? (width + 1) / 2 : width;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < w; ++x) {
          var v = frame.Planes[p][y * frame.Strides[p] + x];
          bytes.Add((byte)v);
          if (ten)
            bytes.Add((byte)(v >> 8));
        }
    }
    return [.. bytes];
  }
}
