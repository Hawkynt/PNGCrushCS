using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.AtariCompressed;

/// <summary>Reads Atari Compressed Screen files from bytes, streams, or file paths.</summary>
public static class AtariCompressedReader {

  public static AtariCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Compressed Screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariCompressedFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 1)
      throw new InvalidDataException("Atari Compressed Screen data is empty.");

    var decompressed = _DecompressRle(data);

    if (decompressed.Length != AtariCompressedFile.DecompressedSize)
      throw new InvalidDataException($"Decompressed size mismatch: expected {AtariCompressedFile.DecompressedSize} bytes, got {decompressed.Length}.");

    return new AtariCompressedFile {
      PixelData = decompressed,
    };
  }

  /// <summary>
  /// Decompresses Atari RLE data.
  /// Byte &gt;= 0x80: repeat count = (byte &amp; 0x7F), next byte = value.
  /// Byte &lt; 0x80: literal byte.
  /// </summary>
  private static byte[] _DecompressRle(byte[] data) {
    var result = new List<byte>(AtariCompressedFile.DecompressedSize);
    var i = 0;

    while (i < data.Length) {
      var b = data[i++];
      if (b >= 0x80) {
        var count = b & 0x7F;
        if (i >= data.Length)
          break;

        var value = data[i++];
        for (var j = 0; j < count; ++j)
          result.Add(value);
      } else
        result.Add(b);
    }

    return result.ToArray();
  }
}
