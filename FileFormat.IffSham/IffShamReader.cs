using System;
using System.IO;

namespace FileFormat.IffSham;

/// <summary>Reads IFF SHAM (Sliced HAM) images from bytes, streams, or file paths.</summary>
public static class IffShamReader {

  public static IffShamFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SHAM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffShamFile FromStream(Stream stream) {
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

  public static IffShamFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffShamFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IffShamFile.MinFileSize)
      throw new InvalidDataException($"Invalid SHAM data: expected at least {IffShamFile.MinFileSize} bytes, got {data.Length}.");

    var width = IffShamFile.DefaultWidth;
    var height = IffShamFile.DefaultHeight;

    // Try to extract dimensions from BMHD chunk if present
    _TryParseBmhd(data, out width, out height);

    var rawData = data.ToArray();

    return new() {
      Width = width,
      Height = height,
      RawData = rawData,
    };
  }

  /// <summary>Attempts to find and parse a BMHD chunk for dimensions.</summary>
  private static void _TryParseBmhd(ReadOnlySpan<byte> data, out int width, out int height) {
    width = IffShamFile.DefaultWidth;
    height = IffShamFile.DefaultHeight;

    // Search for "BMHD" in the data
    for (var i = 0; i < data.Length - 24; ++i) {
      if (data[i] != 0x42 || data[i + 1] != 0x4D || data[i + 2] != 0x48 || data[i + 3] != 0x44)
        continue;

      // BMHD found: skip 4-byte chunk ID + 4-byte size, then read 2-byte BE width + 2-byte BE height
      var offset = i + 8;
      if (offset + 4 > data.Length)
        return;

      width = (data[offset] << 8) | data[offset + 1];
      height = (data[offset + 2] << 8) | data[offset + 3];

      if (width <= 0 || height <= 0) {
        width = IffShamFile.DefaultWidth;
        height = IffShamFile.DefaultHeight;
      }

      return;
    }
  }
}
