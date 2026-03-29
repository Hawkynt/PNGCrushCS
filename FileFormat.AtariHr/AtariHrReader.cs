using System;
using System.IO;

namespace FileFormat.AtariHr;

/// <summary>Reads Atari 8-bit HR hires screen dumps from bytes, streams, or file paths.</summary>
public static class AtariHrReader {

  public static AtariHrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari HR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariHrFile FromStream(Stream stream) {
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

  public static AtariHrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariHrFile.FileSize)
      throw new InvalidDataException($"Data too small for Atari HR screen dump. Expected {AtariHrFile.FileSize} bytes, got {data.Length}.");
    if (data.Length != AtariHrFile.FileSize)
      throw new InvalidDataException($"Invalid Atari HR screen dump size. Expected exactly {AtariHrFile.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AtariHrFile.FileSize];
    data.AsSpan(0, AtariHrFile.FileSize).CopyTo(rawData);

    return new AtariHrFile { RawData = rawData };
  }
}
