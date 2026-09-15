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

  public static SamarHiresMapFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

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
