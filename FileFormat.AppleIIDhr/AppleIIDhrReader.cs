using System;
using System.IO;

namespace FileFormat.AppleIIDhr;

/// <summary>Reads Apple II Double High-Resolution graphics screen dumps from bytes, streams, or file paths.</summary>
public static class AppleIIDhrReader {

  public static AppleIIDhrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Apple II DHGR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleIIDhrFile FromStream(Stream stream) {
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

  public static AppleIIDhrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AppleIIDhrFile.FileSize)
      throw new InvalidDataException($"Invalid Apple II DHGR data size: expected exactly {AppleIIDhrFile.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AppleIIDhrFile.FileSize];
    data.AsSpan(0, AppleIIDhrFile.FileSize).CopyTo(rawData);

    return new AppleIIDhrFile { RawData = rawData };
  }
}
