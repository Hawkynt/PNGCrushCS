using System;
using System.IO;

namespace FileFormat.VertiZontalInterlacing;

/// <summary>Reads VertiZontal Interlacing pictures from bytes, streams, or file paths.</summary>
public static class VertiZontalInterlacingReader {

  public static VertiZontalInterlacingFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VertiZontalInterlacingFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static VertiZontalInterlacingFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != VertiZontalInterlacingFile.FileSize)
      throw new InvalidDataException(
        $"A VertiZontal Interlacing picture is {VertiZontalInterlacingFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static VertiZontalInterlacingFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
