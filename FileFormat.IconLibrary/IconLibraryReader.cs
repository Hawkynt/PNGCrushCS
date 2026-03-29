using System;
using System.IO;

namespace FileFormat.IconLibrary;

/// <summary>Reads Windows Icon Library (ICL) containers from bytes, streams, or file paths.</summary>
public static class IconLibraryReader {

  public static IconLibraryFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Icon Library file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IconLibraryFile FromStream(Stream stream) {
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

  public static IconLibraryFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 6)
      throw new InvalidDataException($"Icon Library data too small: expected at least 6 bytes, got {data.Length}.");

    var width = IconLibraryFile.DefaultSize;
    var height = IconLibraryFile.DefaultSize;

    // Try to extract dimensions from ICO header if present
    if (data.Length >= 22) {
      var reserved = data[0] | (data[1] << 8);
      var type = data[2] | (data[3] << 8);
      if (reserved == 0 && type == 1) {
        var w = data[6];
        var h = data[7];
        if (w > 0)
          width = w;
        if (h > 0)
          height = h;
      }
    }

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new() { Width = width, Height = height, RawData = rawData };
  }
}
