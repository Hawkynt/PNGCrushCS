using System;
using System.IO;

namespace FileFormat.AtariGfb;

/// <summary>Reads Atari 8-bit GFB screen dumps from bytes, streams, or file paths.</summary>
public static class AtariGfbReader {

  public static AtariGfbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari GFB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGfbFile FromStream(Stream stream) {
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

  public static AtariGfbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariGfbFile.FileSize)
      throw new InvalidDataException($"Data too small for Atari GFB screen dump. Expected {AtariGfbFile.FileSize} bytes, got {data.Length}.");
    if (data.Length != AtariGfbFile.FileSize)
      throw new InvalidDataException($"Invalid Atari GFB screen dump size. Expected exactly {AtariGfbFile.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AtariGfbFile.FileSize];
    data.AsSpan(0, AtariGfbFile.FileSize).CopyTo(rawData);

    return new AtariGfbFile { RawData = rawData };
  }
}
