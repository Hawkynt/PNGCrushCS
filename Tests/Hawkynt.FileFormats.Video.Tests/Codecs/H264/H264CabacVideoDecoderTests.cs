using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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

  // x264 core 164, Main profile, 32x16, three frames with one B picture, CABAC, two references, QP 22.
  private const string _CABAC_IPB =
    "AAAAAWdNQAr2XYCIAAADAAgAAAMAMHiRKcAAAAABaOrhMsgAAAEGBf//edxF6b3m2Ui3lizYINkj7u94MjY0IC0gY29yZSAxNjQgcjMxMDggMzFlMTlmOSAtIEguMjY0L01QRUctNCBBVkMgY29kZWMgLSBDb3B5bGVmdCAyMDAzLTIwMjMgLSBodHRwOi8vd3d3LnZpZGVvbGFuLm9yZy94MjY0Lmh0bWwgLSBvcHRpb25zOiBjYWJhYz0xIHJlZj0yIGRlYmxvY2s9MTowOjAgYW5hbHlzZT0weDE6MHgxMTEgbWU9aGV4IHN1Ym1lPTcgcHN5PTEgcHN5X3JkPTEuMDA6MC4wMCBtaXhlZF9yZWY9MSBtZV9yYW5nZT0xNiBjaHJvbWFfbWU9MSB0cmVsbGlzPTEgOHg4ZGN0PTAgY3FtPTAgZGVhZHpvbmU9MjEsMTEgZmFzdF9wc2tpcD0xIGNocm9tYV9xcF9vZmZzZXQ9LTIgdGhyZWFkcz0xIGxvb2thaGVhZF90aHJlYWRzPTEgc2xpY2VkX3RocmVhZHM9MCBucj0wIGRlY2ltYXRlPTEgaW50ZXJsYWNlZD0wIGJsdXJheV9jb21wYXQ9MCBjb25zdHJhaW5lZF9pbnRyYT0wIGJmcmFtZXM9MSBiX3B5cmFtaWQ9MCBiX2FkYXB0PTEgYl9iaWFzPTAgZGlyZWN0PTEgd2VpZ2h0Yj0xIG9wZW5fZ29wPTAgd2VpZ2h0cD0yIGtleWludD0zMCBrZXlpbnRfbWluPTE2IHNjZW5lY3V0PTAgaW50cmFfcmVmcmVzaD0wIHJjPWNxcCBtYnRyZWU9MCBxcD0yMiBpcF9yYXRpbz0xLjQwIHBiX3JhdGlvPTEuMzAgYXE9MACAAAABZYiEA/+6Vyb6z78QLnZXKq044QCaKa0UiZdMLqbgIGBLX533k0OhTEuOlxvQYsLCFDf3nMWEjKbO+GgnvouVo/03VBcH4T5X+ddITiLdUxhXsaADojYSfuSPJvn7zh4WklvtxZyrW6WbE/hjIXvP4QEpyrv6vqXt1AOodaJxCGjIDDfhi453QUF9WxqzQnXvrEpfNL7ZykCJGrBeDixblQ5bhQYLxGzPWAUYsXR+s9YRV+vcVM5v9l2/cQoM0PBASE1zSSH8Q5hnebPZmixKh17KUxa9IqEBclJAhOIFJQgK9DWPiFWrCq+T83tOqIJrXAXdz4BezdgGZZ1PzR7kCLKPhr8CurJv5/bRzriaR1lwdfW/1ZpMqMPlfTngDZcQMsLF6NQ7lG6AsH4ol4FFJ1ER8KrdK9MF691x5u6ypToKrbUD+NlW+DK0rUaU6sHtyWhlgIcq8FcBQlDg+QoamAektveg2FgAtTBSgPU6sKgCCGfP1XdCILBpTjMAsB4y1zVyWaMULAkKj83SchaqQdIZ79lNnJhH/82vyTf2xwDD4yXbytAGfMcoDNQbdzSqWQKcvjghIPqoW9E+0hr5/QoAka7mMIGJKW3PgdkAAAABQZopsf/TLs/8qjrssqJmSdp6vFRpuFoM6r4rb/4oMh4ZBqkN34G/RWBs1mVjhhKgkV//ZUKqcMyD+C+4gWvCW2K3/NMBDYMOfjv99zR1dT8Fr0pHcrnLvVSAeb9GUN8WirLu/UPIZp9lik+OEYcack0j7PsCjZl4AAAAAQGeReSf8RstsCcwkofohX9mZEpHQwfw2eI57Qn2vj4f/f4DH5xB9h5KEIxYz6caCcHQ8sWBSSC3Y5xNnywYwA9jLfV78GfB";

  [Test]
  public void MainProfileCabacIntraFrameMatchesReferenceDecoderPlanes() {
    var frames = _Decode(Convert.FromBase64String(_CABAC_I_FRAME));
    Assert.That(frames, Has.Count.EqualTo(1));
    var decoded = frames[0];
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(decoded.Width, Is.EqualTo(16));
    Assert.That(decoded.Height, Is.EqualTo(16));
    Assert.That(decoded.GetPlaneData(0).ToArray(), Is.All.EqualTo(126));
    Assert.That(decoded.GetPlaneData(1).ToArray(), Is.All.EqualTo(128));
    Assert.That(decoded.GetPlaneData(2).ToArray(), Is.All.EqualTo(128));
  }

  [Test]
  public void MainProfileCabacIPBFramesMatchReferenceDecoderExactly() {
    var frames = _Decode(Convert.FromBase64String(_CABAC_IPB));
    Assert.That(frames, Has.Count.EqualTo(3));
    string[] expected = [
      "2E8F7D3631AC1828537064DB3BFE010650B051ABC0C5565D2EF03250B48DFD34",
      "7EF265DE1D169974B9578107ECC75A9E3C3D8FD446E3FB0F9035B1CBCAFAABE9",
      "A1A4268AF1116BA6F8BF8B904C471E0DF3053124B5D816BE32DCD4AEC698A4BA",
    ];
    for (var index = 0; index < frames.Count; ++index) {
      Assert.That(frames[index].Format, Is.EqualTo(PixelFormat.Yuv420P8), $"frame {index}");
      Assert.That(frames[index].Width, Is.EqualTo(32), $"frame {index}");
      Assert.That(frames[index].Height, Is.EqualTo(16), $"frame {index}");
      Assert.That(Convert.ToHexString(SHA256.HashData(frames[index].PixelData)), Is.EqualTo(expected[index]), $"frame {index}");
    }
  }

  private static List<RawImage> _Decode(byte[] stream) {
    var decoder = H264VideoDecoder.Create(_AnnexBStream);
    var frames = new List<RawImage>();
    foreach (var packet in H264VideoReader.Split(stream))
      if (decoder.TryDecode(packet, out var frame))
        frames.Add(frame);
    frames.AddRange(decoder.Flush());
    return frames;
  }
}
