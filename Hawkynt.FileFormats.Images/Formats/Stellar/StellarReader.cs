using System;
using System.IO;

namespace FileFormat.Stellar;

/// <summary>Reads Stellar pictures from bytes, streams, or file paths.</summary>
public static class StellarReader {

  public static StellarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StellarFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static StellarFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != StellarFile.FileSize)
      throw new InvalidDataException($"A Stellar picture is {StellarFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static StellarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
