using System;
using System.IO;

namespace FileFormat.AtariHr2;

/// <summary>Reads Atari 8-bit HR2 pictures from bytes, streams, or file paths.</summary>
public static class AtariHr2Reader {

  public static AtariHr2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariHr2File FromStream(Stream stream) {
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

  public static AtariHr2File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariHr2File.FileSize)
      throw new InvalidDataException($"An HR2 picture is {AtariHr2File.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static AtariHr2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
