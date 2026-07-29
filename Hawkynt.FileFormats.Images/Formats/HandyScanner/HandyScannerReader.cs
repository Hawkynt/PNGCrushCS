using System;
using System.IO;

namespace FileFormat.HandyScanner;

/// <summary>Reads Handy Scanner scans from bytes, streams, or file paths.</summary>
public static class HandyScannerReader {

  public static HandyScannerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Scan not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HandyScannerFile FromStream(Stream stream) {
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

  public static HandyScannerFile FromSpan(ReadOnlySpan<byte> data) {
    // A whole number of rows and nothing else is the only structure there is to check.
    if (data.Length < HandyScannerFile.BytesPerRow || data.Length % HandyScannerFile.BytesPerRow != 0)
      throw new InvalidDataException(
        $"A scan is a whole number of {HandyScannerFile.BytesPerRow}-byte rows, got {data.Length} bytes.");

    return new() { BitmapData = data.ToArray() };
  }

  public static HandyScannerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
