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

  public static VertiZontalInterlacingFile FromStream(Stream stream) {
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
