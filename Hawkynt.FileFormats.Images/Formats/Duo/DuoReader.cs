using System;
using System.IO;

namespace FileFormat.Duo;

/// <summary>Reads Duo pictures from bytes, streams, or file paths.</summary>
public static class DuoReader {

  public static DuoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Duo picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DuoFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static DuoFile FromSpan(ReadOnlySpan<byte> data) {
    // No header at all: the size is the only identification, and it is exact.
    if (data.Length != DuoFile.FileSize)
      throw new InvalidDataException($"A Duo picture is {DuoFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static DuoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
