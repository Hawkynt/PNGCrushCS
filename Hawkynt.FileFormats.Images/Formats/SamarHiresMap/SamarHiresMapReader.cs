using System;
using System.IO;

namespace FileFormat.SamarHiresMap;

/// <summary>Reads SAMAR Hi-res Interlace with Map of Colours pictures from bytes, streams, or file paths.</summary>
public static class SamarHiresMapReader {

  public static SamarHiresMapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SAMAR picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SamarHiresMapFile FromStream(Stream stream) {
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

  public static SamarHiresMapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SamarHiresMapFile.FileSize)
      throw new InvalidDataException($"Not a SAMAR picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static SamarHiresMapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
