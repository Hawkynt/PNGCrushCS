using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp5.Tests;

/// <summary>
/// The three real VP5 clips committed beside these tests, and FFmpeg's own decode of them.
/// </summary>
/// <remarks>
/// All three are cut out of files published at <c>samples.mplayerhq.hu/V-codecs/VP5/</c> with
/// <c>ffmpeg -c copy</c>, so the bitstream is On2's own and only the container was rewritten.
/// <list type="bullet">
///   <item><description>
///     <c>vp5-sixty-frames</c> — the first sixty frames of <c>potter512-400.avi</c>, 512x304, one key
///     frame and fifty-nine predicted ones, which is the shape that catches a prediction loop drifting.
///   </description></item>
///   <item><description>
///     <c>vp5-three-key-frames</c> — twenty-five frames of the same file from a passage carrying three
///     key frames, so the entropy-model reset and the golden-picture replacement are measured.
///   </description></item>
///   <item><description>
///     <c>vp5-interlaced</c> — ten frames of <c>vp5_interlace.avi</c>, 352x576, the only published
///     interlaced VP5 file and so the only way the field-coded macroblock path can be measured at all.
///   </description></item>
/// </list>
/// Beside each is what FFmpeg makes of it:
/// <code>
/// ffmpeg -i &lt;file&gt; -an -pix_fmt yuv420p -fps_mode passthrough -f framemd5 &lt;file&gt;.framemd5
/// </code>
/// one MD5 a frame over the three planes laid end to end. A digest rather than the planes because the
/// planes are 233 kB a frame and the digest is 32 characters, and anybody can produce the file again
/// from the command above.
/// </remarks>
internal static class Vp5Fixtures {

  internal const string SIXTY_FRAMES = "vp5-sixty-frames";
  internal const string THREE_KEY_FRAMES = "vp5-three-key-frames";
  internal const string INTERLACED = "vp5-interlaced";

  /// <summary>Every coded VP5 packet of a fixture, read through the registry's own container.</summary>
  internal static List<ReadOnlyMemory<byte>> Packets(string fixture) {
    var data = File.ReadAllBytes(Path(fixture + ".avi"));
    var container = VideoFormatRegistry.GetEntry(VideoFormatRegistry.Detect(data));
    Assert.That(container, Is.Not.Null, $"'{fixture}.avi' was not recognised as a container at all.");

    var stream = container!.ReadStreams(data).Single(static s => s.Kind == MediaStreamKind.Video);
    Assert.That(Vp5VideoDecoder.Accepts(stream), Is.True, $"'{fixture}.avi' is not described as VP5.");

    return container.ReadPackets(data)
      .Where(packet => packet.StreamIndex == stream.Index)
      .Select(static packet => packet.Data)
      .ToList();
  }

  /// <summary>Decodes a fixture and hands back every frame's three planes, right way up.</summary>
  internal static List<(byte[] Luma, byte[] Cb, byte[] Cr)> Decode(string fixture) {
    var decoder = new Vp5Decoder();

    return Packets(fixture)
      .Select(packet => decoder.Decode(packet))
      .Select(static picture => (picture.TopDown(0), picture.TopDown(1), picture.TopDown(2)))
      .ToList();
  }

  /// <summary>One frame's checksum, over its three planes laid end to end.</summary>
  internal static string Digest((byte[] Luma, byte[] Cb, byte[] Cr) planes) {
    using var md5 = MD5.Create();
    md5.TransformBlock(planes.Luma, 0, planes.Luma.Length, null, 0);
    md5.TransformBlock(planes.Cb, 0, planes.Cb.Length, null, 0);
    md5.TransformFinalBlock(planes.Cr, 0, planes.Cr.Length);

    return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
  }

  /// <summary>FFmpeg's own per-frame checksums, in frame order.</summary>
  internal static List<string> ExpectedFrameDigests(string fixture)
    => ParseFrameMd5(File.ReadAllLines(Path(fixture + ".framemd5")));

  internal static List<string> ParseFrameMd5(IEnumerable<string> lines) => lines
    .Where(static line => !line.StartsWith('#') && line.Trim().Length > 0)
    .Select(static line => line.Split(',').Last().Trim())
    .ToList();

  internal static string Path(string fileName) {
    var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vp5", fileName);
    Assert.That(File.Exists(path), Is.True, $"The VP5 fixture '{fileName}' was not copied beside the test assembly.");

    return path;
  }
}
