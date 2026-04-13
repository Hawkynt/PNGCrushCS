using System;
using System.IO;

namespace FileFormat.AppleIIHgr;

/// <summary>Reads Apple II High-Resolution graphics screen dumps from bytes, streams, or file paths.</summary>
public static class AppleIIHgrReader {

  public static AppleIIHgrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Apple II HGR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleIIHgrFile FromStream(Stream stream) {
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

  public static AppleIIHgrFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != AppleIIHgrFile.FileSize)
      throw new InvalidDataException($"Invalid Apple II HGR data size: expected exactly {AppleIIHgrFile.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AppleIIHgrFile.FileSize];
    data.Slice(0, AppleIIHgrFile.FileSize).CopyTo(rawData);

    return new AppleIIHgrFile { RawData = rawData };
    }

  public static AppleIIHgrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AppleIIHgrFile.FileSize)
      throw new InvalidDataException($"Invalid Apple II HGR data size: expected exactly {AppleIIHgrFile.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[AppleIIHgrFile.FileSize];
    data.AsSpan(0, AppleIIHgrFile.FileSize).CopyTo(rawData);

    return new AppleIIHgrFile { RawData = rawData };
  }
}
