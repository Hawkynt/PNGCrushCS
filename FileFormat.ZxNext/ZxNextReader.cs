using System;
using System.IO;

namespace FileFormat.ZxNext;

/// <summary>Reads ZX Spectrum Next (.nxt) files from bytes, streams, or file paths.</summary>
public static class ZxNextReader {

  /// <summary>Fixed file size: 256 * 192 = 49152 bytes at 8bpp.</summary>
  internal const int FileSize = 49152;

  /// <summary>Image width.</summary>
  internal const int ImageWidth = 256;

  /// <summary>Image height.</summary>
  internal const int ImageHeight = 192;

  public static ZxNextFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Next file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxNextFile FromStream(Stream stream) {
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

  public static ZxNextFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Next file must be exactly {FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[FileSize];
    data.AsSpan(0, FileSize).CopyTo(pixelData);

    return new ZxNextFile {
      PixelData = pixelData,
    };
  }
}
