using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using NUnit.Framework;

namespace FileFormat.Bpg.Tests;

/// <summary>BPG files <c>bpgenc</c> 0.9.8 wrote, decoded to exactly what libbpg decodes them to.</summary>
/// <remarks>
/// The hash beside each file is of libbpg's own output — every frame, RGB or RGBA, eight bits a
/// sample for an eight-bit file and sixteen (little-endian) for a deeper one, as
/// <c>bpg_decoder_get_line</c> writes it — taken with a small harness on libbpg's decoding API so
/// that every frame of an animation is seen, not only the first that <c>bpgdec</c> writes. The
/// decode here is put into the same layout and hashed. Between them the files cover 4:2:0 and
/// 4:2:2 in both chroma positions' default, grey, the BT.601, BT.709, BT.2020 and YCgCo matrices,
/// limited range, ten and twelve bits, alpha straight and premultiplied, and two animations whose
/// later frames are P pictures, one with an alpha stream and a frame duration of its own per frame.
/// The 4:4:4, RGB and lossless files exercise the H.265 range extension tools <c>bpgenc</c> turns on
/// for them: cross-component prediction and implicit RDPCM.
/// </remarks>
[TestFixture]
public sealed class LibbpgParityTests {

  private static byte[] _Fixture(string name)
    => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "BpgFixtures", name + ".bpg"));

  [TestCase("libbpg_rgb61_420_ycbcr", "10c1ec4f7d789453c0c19508e4cf7cfaecce79031fd11edfeff67bc7b0db691f")]
  [TestCase("libbpg_rgb61_422_ycbcr_bt709", "19ef6843c54350124bdaae852479a4480715729f3bd4e8bc874734542595479e")]
  [TestCase("libbpg_rgb61_420_ycgco", "c2789116043bd80a9f8abc33defe05eb64dfd12bd1a002a3199f63e5f97a8339")]
  [TestCase("libbpg_rgb61_420_limited", "50b2a86b797aa732aef8d4ed887e4653d230ae48a3c7e680ec004c2f612a4d95")]
  [TestCase("libbpg_rgb61_420_b10", "2167112b221f184f85da132bfab6e3db91075e9513d246edd5804885360315a7")]
  [TestCase("libbpg_rgb61_422_ycbcr_bt2020", "3e74605bb3b093ec1c7ac12d343273c44f7ddcc00c8bfb389e377cc8ad0f47b0")]
  [TestCase("libbpg_gray61", "1bf8ce6369c5e41beea49433cf8e75d65ad44c58f884420168ea7cdc01d895ce")]
  [TestCase("libbpg_gray61_b12", "16664c17c4b866fc5898003253f533c066635eb5cfd02707cd49e48826dd5710")]
  [TestCase("libbpg_rgba61_420", "da7d587705eb644ba6747e846faf531270dc2a9555f76626e2fd9369865ad544")]
  [TestCase("libbpg_rgba61_420_premul", "766679569f4b933d8b2a5a4b5eafe359344f54a9c558abff3c95b96a07cbcb7e")]
  [TestCase("libbpg_rgba61_422_b10", "d43204ec29bd8855270012c35d8c1327ce72db530bba21592fd983f48a392eab")]
  [TestCase("libbpg_anim_rgba_delays", "130cf630e1bed74fa6188440e6579cb3bddfbbe196fa978c6c968dc21158a0f9")]
  [TestCase("libbpg_anim_rgb", "584eed06507fdc080528a513e1d2119ac2a0a2ac40ef8708cf6f38d0b457d62b")]
  [TestCase("libbpg_rgb61_444_ycbcr", "377ecdad3ade1f62f86b58f50df157a61ad388988426e131e704875bb4d54f53")]
  [TestCase("libbpg_rgb61_444_rgb", "8cdb7b40001678cc469e2b3f844d09b655822de79e0fb607ebb766c6cf795455")]
  [TestCase("libbpg_rgb61_444_b12", "ce05851cd1032f820fdb1f0a5ac0483eec3f7b876afe53ba6d906ba0808043c6")]
  [TestCase("libbpg_rgb61_lossless", "8bcbe17c0f3eeb0bde5f6d6394935f40ac2b6ceb8787abc00bea26e75f71cf2c")]
  [TestCase("libbpg_rgba61_444_premul", "62cf7bdb8c2a2a8dec09954dbd500568ed9bdf17336106984a2db53d79c7c4a9")]
  [TestCase("libbpg_rgba61_lossless", "932cf9c130b6efa4040edc6990ea7b32151eba00c5536089ffaea9a4fbe6ac4c")]
  [Category("Unit")]
  public void DecodesEveryFrameAsLibbpgDoes(string name, string sha256) {
    var file = BpgReader.FromBytes(_Fixture(name));
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    for (var i = 0; i < BpgFile.ImageCount(file); ++i)
      hash.AppendData(_LibbpgLayout(BpgFile.ToRawImage(file, i)));
    Assert.That(Convert.ToHexStringLower(hash.GetHashAndReset()), Is.EqualTo(sha256));
  }

  [Test]
  [Category("Unit")]
  public void AnAnimationTakesEachFramesOwnDuration() {
    var file = BpgReader.FromBytes(_Fixture("libbpg_anim_rgba_delays"));
    var durations = BpgFile.FrameDurations(file).Select(static d => Math.Round(d.TotalSeconds * 25)).ToArray();
    Assert.Multiple(() => {
      Assert.That(BpgFile.ImageCount(file), Is.EqualTo(4));
      Assert.That(durations, Is.EqualTo(new double[] { 2, 5, 2, 8 }));
      Assert.That(BpgReader.FromBytes(_Fixture("libbpg_anim_rgb")).LoopCount, Is.EqualTo(3));
    });
  }

  /// <summary>libbpg's raster: grey widened to RGB, sixteen-bit samples little-endian.</summary>
  private static byte[] _LibbpgLayout(RawImage image) {
    var (parts, bytes, gray) = image.Format switch {
      PixelFormat.Gray8 => (1, 1, true),
      PixelFormat.Gray16 => (1, 2, true),
      PixelFormat.Rgb24 => (3, 1, false),
      PixelFormat.Rgba32 => (4, 1, false),
      PixelFormat.Rgb48 => (3, 2, false),
      PixelFormat.Rgba64 => (4, 2, false),
      _ => throw new NotSupportedException(image.Format.ToString()),
    };
    var outParts = gray ? 3 : parts;
    var result = new byte[image.Width * image.Height * outParts * bytes];
    for (var p = 0; p < image.Width * image.Height; ++p)
      for (var c = 0; c < outParts; ++c) {
        var from = (p * parts + (gray ? 0 : c)) * bytes;
        var to = (p * outParts + c) * bytes;
        if (bytes == 1)
          result[to] = image.PixelData[from];
        else {
          result[to] = image.PixelData[from + 1];
          result[to + 1] = image.PixelData[from];
        }
      }

    return result;
  }
}
