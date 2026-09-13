using System;
using System.IO;

namespace FileFormat.AppleSh3;

/// <summary>Reads unpacked 3200-colour pictures from bytes, streams, or file paths.</summary>
public static class AppleSh3Reader {

  public static AppleSh3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleSh3File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static AppleSh3File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AppleSh3File.FileSize)
      throw new InvalidDataException($"Not an unpacked 3200-colour picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static AppleSh3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
