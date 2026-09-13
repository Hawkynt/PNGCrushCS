using System;
using System.IO;

namespace FileFormat.FunPhotor;

/// <summary>Reads FunPhotor frames from bytes, streams, or file paths.</summary>
public static class FunPhotorReader {

  private static ReadOnlySpan<byte> _PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  public static FunPhotorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FunPhotor frame not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunPhotorFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static FunPhotorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= FunPhotorFile.HeaderSize + _PngSignature.Length)
      throw new InvalidDataException($"Data too small for a FunPhotor frame (got {data.Length} bytes).");

    if (!data.Slice(FunPhotorFile.HeaderSize, _PngSignature.Length).SequenceEqual(_PngSignature))
      throw new InvalidDataException("A FunPhotor frame carries a PNG four bytes in; this file does not.");

    return new() { Embedded = data[FunPhotorFile.HeaderSize..].ToArray() };
  }

  public static FunPhotorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
