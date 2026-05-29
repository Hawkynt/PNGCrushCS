using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Ics;

/// <summary>Assembles ICS (Image Cytometry Standard) file bytes from pixel data.</summary>
public static class IcsWriter {

  public static byte[] ToBytes(IcsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    // Default version to "2.0" if not set
    var effectiveFile = string.IsNullOrEmpty(file.Version)
      ? file with { Version = "2.0", PixelData = file.PixelData ?? Array.Empty<byte>() }
      : file with { PixelData = file.PixelData ?? Array.Empty<byte>() };
    return _Assemble(effectiveFile);
  }

  private static byte[] _Assemble(IcsFile file) {
    using var ms = new MemoryStream();

    // Write header
    var header = IcsHeaderParser.Format(file);
    ms.Write(header);

    // Compress and write pixel data
    if (file.Compression == IcsCompression.Gzip) {
      var compressedData = _CompressGzip(file.PixelData);
      ms.Write(compressedData);
    } else
      ms.Write(file.PixelData);

    return ms.ToArray();
  }

  private static byte[] _CompressGzip(byte[] data) {
    using var outputStream = new MemoryStream();
    using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
      gzipStream.Write(data);

    return outputStream.ToArray();
  }
}
