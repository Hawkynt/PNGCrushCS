using System;
using System.IO;
using System.IO.Compression;
using Compression.Core;

namespace FileFormat.Tiff;

/// <summary>TIFF Deflate/zlib compression helper.</summary>
internal static class TiffDeflateHelper {

  /// <summary>Decompresses zlib-wrapped DEFLATE data.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength) {
    using var input = new MemoryStream(data.ToArray());
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    var output = new byte[expectedLength];
    var totalRead = 0;
    while (totalRead < expectedLength) {
      var read = zlib.Read(output, totalRead, expectedLength - totalRead);
      if (read == 0)
        break;
      totalRead += read;
    }

    return output;
  }

  /// <summary>Compresses data using standard zlib DEFLATE.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize))
      zlib.Write(data);

    return output.ToArray();
  }

  /// <summary>Compresses data using Zopfli (zlib-wrapped).</summary>
  public static byte[] CompressZopfli(ReadOnlySpan<byte> data, bool hyper, int iterations)
    => ZopfliDeflater.Compress(data.ToArray(), hyper, iterations);
}
