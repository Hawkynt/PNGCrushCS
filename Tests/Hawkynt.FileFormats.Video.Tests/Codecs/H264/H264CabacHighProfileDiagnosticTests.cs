using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using FileFormat.Core;
using FileFormat.H264Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264CabacHighProfileDiagnosticTests {
  [Test]
  public void ReportAllHighProfileIpbbHashes() {
    var field = typeof(H264CabacHighProfileConformanceTests).GetField(
      "_CABAC_HIGH_8X8_IPBB",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.That(field, Is.Not.Null);
    var encoded = (string)field!.GetRawConstantValue()!;
    var encodedBytes = Convert.FromBase64String(encoded);

    var diagnosticDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "H264Diagnostic");
    Directory.CreateDirectory(diagnosticDirectory);
    File.WriteAllBytes(Path.Combine(diagnosticDirectory, "high-profile-ipbb.h264"), encodedBytes);

    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
    };
    var decoder = H264VideoDecoder.Create(stream);
    var frames = new List<RawImage>();
    foreach (var packet in H264VideoReader.Split(encodedBytes))
      if (decoder.TryDecode(packet, out var frame))
        frames.Add(frame);
    frames.AddRange(decoder.Flush());

    var hashes = new string[frames.Count];
    for (var index = 0; index < frames.Count; ++index) {
      hashes[index] = Convert.ToHexString(SHA256.HashData(frames[index].PixelData));
      File.WriteAllBytes(Path.Combine(diagnosticDirectory, $"high-profile-ipbb-frame-{index}.yuv"), frames[index].PixelData);
    }

    Assert.Fail($"High-profile IPBB hashes: {string.Join(", ", hashes)}");
  }
}
