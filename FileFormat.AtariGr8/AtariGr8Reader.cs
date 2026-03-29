using System;
using System.IO;

namespace FileFormat.AtariGr8;

/// <summary>Reads Atari 8-bit Graphics Mode 8 screen dumps from bytes, streams, or file paths.</summary>
public static class AtariGr8Reader {

  public static AtariGr8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari GR.8 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGr8File FromStream(Stream stream) {
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

  public static AtariGr8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariGr8File.FileSize)
      throw new InvalidDataException($"Data too small for Atari GR.8 screen dump. Expected {AtariGr8File.FileSize} bytes, got {data.Length}.");
    if (data.Length != AtariGr8File.FileSize)
      throw new InvalidDataException($"Invalid Atari GR.8 screen dump size. Expected exactly {AtariGr8File.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AtariGr8File.FileSize];
    data.AsSpan(0, AtariGr8File.FileSize).CopyTo(rawData);

    return new AtariGr8File { RawData = rawData };
  }
}
