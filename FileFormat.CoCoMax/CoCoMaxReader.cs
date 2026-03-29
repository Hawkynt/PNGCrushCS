using System;
using System.IO;

namespace FileFormat.CoCoMax;

/// <summary>Reads CoCoMax paint program images from bytes, streams, or file paths.</summary>
public static class CoCoMaxReader {

  public static CoCoMaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CoCoMax file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CoCoMaxFile FromStream(Stream stream) {
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

  public static CoCoMaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CoCoMaxFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CoCoMax data size: expected exactly {CoCoMaxFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CoCoMaxFile.ExpectedFileSize];
    data.AsSpan(0, CoCoMaxFile.ExpectedFileSize).CopyTo(rawData);

    return new CoCoMaxFile { RawData = rawData };
  }
}
