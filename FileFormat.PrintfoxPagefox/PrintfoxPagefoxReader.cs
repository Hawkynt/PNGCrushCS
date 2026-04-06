using System;
using System.IO;

namespace FileFormat.PrintfoxPagefox;

/// <summary>Reads Printfox/Pagefox (.bs/.pg) files from bytes, streams, or file paths.</summary>
public static class PrintfoxPagefoxReader {

  public static PrintfoxPagefoxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Printfox/Pagefox file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintfoxPagefoxFile FromStream(Stream stream) {
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

  public static PrintfoxPagefoxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PrintfoxPagefoxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PrintfoxPagefoxFile.MinDataSize)
      throw new InvalidDataException($"Data too small for a valid Printfox/Pagefox file (expected at least {PrintfoxPagefoxFile.MinDataSize} bytes, got {data.Length}).");

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new() {
      RawData = rawData,
    };
  }
}
