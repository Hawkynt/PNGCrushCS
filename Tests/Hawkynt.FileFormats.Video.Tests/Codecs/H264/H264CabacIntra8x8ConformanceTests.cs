using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using FileFormat.Core;
using FileFormat.H264Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264CabacIntra8x8ConformanceTests {
  private static readonly MediaStreamInfo _AnnexBStream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("avc1"),
  };

  // x264 core 164, High profile, progressive 8-bit 4:2:0, 64x64, one IDR picture, QP 20,
  // CABAC and 8x8 transform enabled. x264 reports 12.5% intra 8x8-transform use, so this
  // independently exercises CABAC transform_size_8x8_flag plus Intra8x8 residual syntax.
  // FFmpeg 7.1.5 produced the native YUV420 oracle hash below.
  private const string _CABAC_HIGH_INTRA_8X8 =
    "AAAAAWdkEAqsuITYCIAAAAMAgAAAAwECAAAAAWjuBssiwAAAAQYF//8s3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE2NCByMzEwOCAzMWUxOWY5IC0gSC4yNjQvTVBFRy00IEFWQyBjb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyMyAtIGh0dHA6Ly93d3cudmlkZW9sYW4ub3JnL3gyNjQuaHRtbCAtIG9wdGlvbnM6IGNhYmFjPTEgcmVmPTEgZGVibG9jaz0xOjA6MCBhbmFseXNlPTB4MzoweDEzMyBtZT11bWggc3VibWU9OSBwc3k9MSBwc3lfcmQ9MS4wMDowLjAwIG1peGVkX3JlZj0wIG1lX3JhbmdlPTI0IGNocm9tYV9tZT0xIHRyZWxsaXM9MiA4eDhkY3Q9MSBjcW09MCBkZWFkem9uZT0yMSwxMSBmYXN0X3Bza2lwPTEgY2hyb21hX3FwX29mZnNldD0tMiB0aHJlYWRzPTEgbG9va2FoZWFkX3RocmVhZHM9MSBzbGljZWRfdGhyZWFkcz0wIG5yPTAgZGVjaW1hdGU9MSBpbnRlcmxhY2VkPTAgYmx1cmF5X2NvbXBhdD0wIGNvbnN0cmFpbmVkX2ludHJhPTAgYmZyYW1lcz0wIHdlaWdodHA9MCBrZXlpbnQ9MSBrZXlpbnRfbWluPTEgc2NlbmVjdXQ9MCBpbnRyYV9yZWZyZXNoPTAgcmM9Y3FwIG1idHJlZT0wIHFwPTIwIGlwX3JhdGlvPTEuNDAgYXE9MACAAAABZYiEP+Rb+PQv4PMvPgBkMNG038HLfszBSsZ1a4zrMjhR2fiWP7mFUFxWudYoaZndysk85eE8KaIPfvx8EbCgpJK09EnzGuFT1pL2rqz+kkWSBmsGrX0hnZYHHnSQl1ESDnc3i2IKmykk1xGpCuwOmh7nqcNAMWpGTE+PfHfHR6VWFGjT9uQuqgePlBcfjU5jwh0lajBAhbFxvMCXMQ==";

  [Test]
  public void HighProfileCabacIntra8x8MatchesReferenceDecoderExactly() {
    var frames = _Decode(Convert.FromBase64String(_CABAC_HIGH_INTRA_8X8));
    Assert.That(frames, Has.Count.EqualTo(1));
    var frame = frames[0];
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(frame.Width, Is.EqualTo(64));
    Assert.That(frame.Height, Is.EqualTo(64));
    Assert.That(
      Convert.ToHexString(SHA256.HashData(frame.PixelData)),
      Is.EqualTo("F237D135D5CAEE6D627B5330F70904FDAD374D1C212C98FE3AABF7D9B3AC75ED"));
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
