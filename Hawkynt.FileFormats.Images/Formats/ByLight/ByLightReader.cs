using System;
using System.IO;

namespace FileFormat.ByLight;

/// <summary>Reads byLight images from bytes, streams, or file paths.</summary>
public static class ByLightReader {

  public static ByLightFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ByLightFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static ByLightFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ByLightFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ByLightFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid BIF file (need at least {ByLightFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != ByLightFile.Magic[0] || data[1] != ByLightFile.Magic[1])
      throw new InvalidDataException("Invalid BIF magic bytes.");

    var payload = data[ByLightFile.HeaderSize..];
    if (payload[0] != 0xFF || payload[1] != 0xD8)
      throw new InvalidDataException("BIF file carries no JPEG stream at offset 374.");

    return new() {
      Header = data[..ByLightFile.HeaderSize].ToArray(),
      JpegData = payload.ToArray(),
    };
  }

}
