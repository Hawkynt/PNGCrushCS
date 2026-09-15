using System;
using System.IO;
using FileFormat.Codecs;
using FileFormat.Core;

namespace FileFormat.Codecs.H261.Tests;

[TestFixture]
public sealed class H261DiagnosticDumpTests {
  [Test]
  public void DumpRegistryFirstPacket() {
    const int width = 176;
    const int height = 144;
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      data[at] = (byte)(x * 255 / (width - 1));
      data[at + 1] = (byte)(y * 255 / (height - 1));
      data[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("H261"),
      Width = width, Height = height, TimeBase = new(1, 25), FrameRate = new(25, 1),
    };
    var encoder = H261VideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data }, 0, out var packet), Is.True);
    File.WriteAllBytes("/tmp/h261-registry-first.h261", packet.Data.ToArray());
    TestContext.Progress.WriteLine($"bytes={packet.Data.Length} hex={Convert.ToHexString(packet.Data.Span[..Math.Min(packet.Data.Length, 512)])}");
  }
}
