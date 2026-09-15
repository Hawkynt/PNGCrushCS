using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;

namespace FileFormat.Ogg.Tests;

[TestFixture]
public sealed class OggTheoraEncoderTests {

  [Test]
  [Category("Unit")]
  public void MatroskasTheoraNameStillGetsTheorasGranuleEncoding() {
    const int width = 16;
    const int height = 16;
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("Theo"),
      FrameRate = new Rational(25, 1),
      TimeBase = new Rational(1, 25),
      Width = width,
      Height = height,
    };
    var encoder = TheoraVideoEncoder.Create(requested);
    var frame = _Flat(width, height);

    encoder.TryEncode(frame, 0, out var first);
    encoder.TryEncode(frame, 1, out var second);
    var file = VideoIO.Mux<OggWriter>([encoder.DescribeStream()], [first, second]);
    var granules = _Granules(file).ToArray();

    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().CodecId, Is.EqualTo("V_THEORA"));
      Assert.That(granules[^2], Is.EqualTo(1L << 6));
      Assert.That(granules[^1], Is.EqualTo(2L << 6));
    });
  }

  private static RawImage _Flat(int width, int height) {
    var samples = width * height;
    var planes = new byte[samples * 3];
    planes.AsSpan(0, samples).Fill(128);
    planes.AsSpan(samples).Fill(128);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = planes };
  }

  private static IEnumerable<long> _Granules(byte[] file) {
    var at = 0;
    while (at < file.Length) {
      Assert.That(file.AsSpan(at, 4).SequenceEqual("OggS"u8), Is.True, $"page at {at}");
      var segmentCount = file[at + 26];
      var bodyLength = 0;
      for (var segment = 0; segment < segmentCount; ++segment)
        bodyLength += file[at + 27 + segment];

      yield return BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(at + 6, 8));
      at += 27 + segmentCount + bodyLength;
    }
  }
}
