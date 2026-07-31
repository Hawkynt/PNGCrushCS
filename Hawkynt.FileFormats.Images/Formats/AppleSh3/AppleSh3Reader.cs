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

  public static AppleSh3File FromStream(Stream stream) {
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
