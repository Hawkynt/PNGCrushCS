using System;
using System.IO;

namespace FileFormat.RawWorkshop;

/// <summary>Reads Raw Workshop dumps from bytes, streams, or file paths.</summary>
public static class RawWorkshopReader {

  public static RawWorkshopFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RawWorkshopFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>The length is the whole of the identification: there is nothing else in the file.</summary>
  public static RawWorkshopFile FromSpan(ReadOnlySpan<byte> data) {
    foreach (var (length, width, height) in RawWorkshopFile.Screens) {
      if (data.Length != length)
        continue;

      return new() { Width = width, Height = height, Pixels = data.ToArray() };
    }

    throw new InvalidDataException($"A Raw Workshop dump is 64000, 128000 or 256000 bytes, got {data.Length}.");
  }

  public static RawWorkshopFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
