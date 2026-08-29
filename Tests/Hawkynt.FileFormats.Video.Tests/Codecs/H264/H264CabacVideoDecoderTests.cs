using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.H264Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264CabacVideoDecoderTests {
  private static readonly MediaStreamInfo _AnnexBStream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("avc1"),
  };

  // x264 core 164, Main profile, 16x16, one IDR picture, QP 20, CABAC enabled.
  // The decoded oracle was produced independently by FFmpeg 7.1.5 and compared as native YUV420 planes.
  private const string _CABAC_I_FRAME =
    "AAAAAWdNQArd7ARAAAADAEAAAAMAg8SJ4AAAAAFo7gbLIAAAAQYF//8s3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE2NCByMzEwOCAzMWUxOWY5IC0gSC4yNjQvTVBFRy00IEFWQyBjb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyMyAtIGh0dHA6Ly93d3cudmlkZW9sYW4ub3JnL3gyNjQuaHRtbCAtIG9wdGlvbnM6IGNhYmFjPTEgcmVmPTEgZGVibG9jaz0xOjA6MCBhbmFseXNlPTB4MToweDExMSBtZT1oZXggc3VibWU9NyBwc3k9MSBwc3lfcmQ9MS4wMDowLjAwIG1peGVkX3JlZj0wIG1lX3JhbmdlPTE2IGNocm9tYV9tZT0xIHRyZWxsaXM9MSA4eDhkY3Q9MCBjcW09MCBkZWFkem9uZT0yMSwxMSBmYXN0X3Bza2lwPTEgY2hyb21hX3FwX29mZnNldD0tMiB0aHJlYWRzPTEgbG9va2FoZWFkX3RocmVhZHM9MSBzbGljZWRfdGhyZWFkcz0wIG5yPTAgZGVjaW1hdGU9MSBpbnRlcmxhY2VkPTAgYmx1cmF5X2NvbXBhdD0wIGNvbnN0cmFpbmVkX2ludHJhPTAgYmZyYW1lcz0wIHdlaWdodHA9MCBrZXlpbnQ9MSBrZXlpbnRfbWluPTEgc2NlbmVjdXQ9MCBpbnRyYV9yZWZyZXNoPTAgcmM9Y3FwIG1idHJlZT0wIHFwPTIwIGlwX3JhdGlvPTEuNDAgYXE9MACAAAABZYiEP/70oP//";

  [Test]
  public void MainProfileCabacIntraFrameMatchesReferenceDecoderPlanes() {
    var decoder = H264VideoDecoder.Create(_AnnexBStream);
    var frames = new List<RawImage>();
    foreach (var packet in H264VideoReader.Split(Convert.FromBase64String(_CABAC_I_FRAME)))
      if (decoder.TryDecode(packet, out var frame))
        frames.Add(frame);
    frames.AddRange(decoder.Flush());

    Assert.That(frames, Has.Count.EqualTo(1));
    var decoded = frames[0];
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(decoded.Width, Is.EqualTo(16));
    Assert.That(decoded.Height, Is.EqualTo(16));
    Assert.That(decoded.GetPlaneData(0).ToArray(), Is.All.EqualTo(126));
    Assert.That(decoded.GetPlaneData(1).ToArray(), Is.All.EqualTo(128));
    Assert.That(decoded.GetPlaneData(2).ToArray(), Is.All.EqualTo(128));
  }
}
