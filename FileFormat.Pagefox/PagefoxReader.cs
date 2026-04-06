using System;
using System.IO;

namespace FileFormat.Pagefox;

/// <summary>Reads Pagefox hires files from bytes, streams, or file paths.</summary>
public static class PagefoxReader {

  public static PagefoxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pagefox file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PagefoxFile FromStream(Stream stream) {
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

  public static PagefoxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PagefoxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PagefoxFile.ExpectedFileSize)
      throw new InvalidDataException($"Pagefox file too small (got {data.Length} bytes, expected {PagefoxFile.ExpectedFileSize}).");

    var rawData = new byte[PagefoxFile.ExpectedFileSize];
    data.AsSpan(0, PagefoxFile.ExpectedFileSize).CopyTo(rawData.AsSpan(0));

    return new() { RawData = rawData };
  }
}
