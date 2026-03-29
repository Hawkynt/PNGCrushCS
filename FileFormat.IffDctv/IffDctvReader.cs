using System;
using System.IO;

namespace FileFormat.IffDctv;

/// <summary>Reads IFF DCTV (Composite Video) images from bytes, streams, or file paths.</summary>
public static class IffDctvReader {

  public static IffDctvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DCTV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffDctvFile FromStream(Stream stream) {
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

  public static IffDctvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < IffDctvFile.MinFileSize)
      throw new InvalidDataException($"Invalid DCTV data: expected at least {IffDctvFile.MinFileSize} bytes, got {data.Length}.");

    var width = IffDctvFile.DefaultWidth;
    var height = IffDctvFile.DefaultHeight;

    _TryParseBmhd(data, out width, out height);

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new() {
      Width = width,
      Height = height,
      RawData = rawData,
    };
  }

  private static void _TryParseBmhd(byte[] data, out int width, out int height) {
    width = IffDctvFile.DefaultWidth;
    height = IffDctvFile.DefaultHeight;

    for (var i = 0; i < data.Length - 24; ++i) {
      if (data[i] != 0x42 || data[i + 1] != 0x4D || data[i + 2] != 0x48 || data[i + 3] != 0x44)
        continue;

      var offset = i + 8;
      if (offset + 4 > data.Length)
        return;

      width = (data[offset] << 8) | data[offset + 1];
      height = (data[offset + 2] << 8) | data[offset + 3];

      if (width <= 0 || height <= 0) {
        width = IffDctvFile.DefaultWidth;
        height = IffDctvFile.DefaultHeight;
      }

      return;
    }
  }
}
