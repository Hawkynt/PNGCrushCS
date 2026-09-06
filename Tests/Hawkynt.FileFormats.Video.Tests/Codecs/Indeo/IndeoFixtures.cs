using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The two real Indeo files committed beside these tests, and ffmpeg's own decode of them.
/// </summary>
/// <remarks>
/// Six frames apiece of <c>IHIGHPER.AVI</c> and <c>cubes.mov</c> from
/// <c>samples.ffmpeg.org/V-codecs/</c>, remuxed by ffmpeg with the video stream copied untouched, so
/// the bitstream is the original and only the container was rewritten. Beside each is what ffmpeg
/// makes of it:
/// <code>
/// ffmpeg -i &lt;file&gt; -an -pix_fmt yuv410p -fps_mode passthrough -f framemd5 &lt;file&gt;.framemd5
/// </code>
/// which is one MD5 per frame over the three planes as the codec codes them, laid end to end. That is
/// the expected output for these tests. A digest rather than the planes themselves because the planes
/// are 130 kB a file and the digest is 32 characters a frame, and anybody can produce the same file
/// again from the command above.
/// <para/>
/// Comparing the planes and not the RGB picture is deliberate, and the same choice the decoders' own
/// remarks explain: the RGB is a display convention these decoders chose, and the planes are the
/// decode.
/// </remarks>
internal static class IndeoFixtures {

  /// <summary>The six-frame Indeo 2 file, its stream description and its coded frames.</summary>
  internal const string INDEO_2 = "rt21-six-frames";

  /// <summary>The six-frame Indeo 3 file.</summary>
  internal const string INDEO_3 = "iv32-six-frames";

  /// <summary>Reads a fixture's video stream and every coded frame in it, through the registry.</summary>
  internal static (MediaStreamInfo Stream, List<CodedPacket> Packets) Read(string name) {
    var data = File.ReadAllBytes(_Path(name + ".avi"));

    var format = VideoFormatRegistry.Detect(data);
    var container = VideoFormatRegistry.GetEntry(format);
    Assert.That(container, Is.Not.Null, $"'{name}.avi' was not recognised as a container at all.");

    var stream = container!.ReadStreams(data).Single(s => s.Kind == MediaStreamKind.Video);
    var packets = container.ReadPackets(data).Where(p => p.StreamIndex == stream.Index).ToList();

    return (stream, packets);
  }

  /// <summary>ffmpeg's own per-frame checksums, in frame order.</summary>
  internal static List<string> ExpectedFrames(string name) {
    var lines = File.ReadAllLines(_Path(name + ".framemd5"));

    return lines
      .Where(line => !line.StartsWith('#') && line.Trim().Length > 0)
      .Select(line => line.Split(',').Last().Trim())
      .ToList();
  }

  /// <summary>The checksum of one decoded frame, over its three planes laid end to end.</summary>
  internal static string Checksum(byte[] luma, byte[] cb, byte[] cr) {
    using var md5 = MD5.Create();
    md5.TransformBlock(luma, 0, luma.Length, null, 0);
    md5.TransformBlock(cb, 0, cb.Length, null, 0);
    md5.TransformFinalBlock(cr, 0, cr.Length);

    return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
  }

  private static string _Path(string fileName) {
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Indeo", fileName);
    Assert.That(File.Exists(path), Is.True, $"The Indeo fixture '{fileName}' was not copied beside the test assembly.");

    return path;
  }
}
