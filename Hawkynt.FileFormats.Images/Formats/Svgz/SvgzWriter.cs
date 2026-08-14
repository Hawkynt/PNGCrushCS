using System.IO;
using System.IO.Compression;
using FileFormat.Svg;

namespace FileFormat.Svgz;

/// <summary>Serialises a drawing and gzips it.</summary>
public static class SvgzWriter {

  /// <summary>The drawing, gzipped.</summary>
  /// <remarks>
  /// Smallest rather than fastest: the point of the format is the size, and the markup a drawing is
  /// made of compresses far enough that the difference is worth the time.
  /// </remarks>
  public static byte[] ToBytes(SvgzFile file) {
    var markup = SvgWriter.ToBytes(file.Drawing);

    using var memory = new MemoryStream();
    using (var gzip = new GZipStream(memory, CompressionLevel.SmallestSize, leaveOpen: true))
      gzip.Write(markup, 0, markup.Length);

    return memory.ToArray();
  }
}
