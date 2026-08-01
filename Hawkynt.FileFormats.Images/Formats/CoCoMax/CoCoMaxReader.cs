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

  public static CoCoMaxFile FromSpan(ReadOnlySpan<byte> data) {

    var legal = false;
    foreach (var size in CoCoMaxFile.LegalSizes)
      legal |= data.Length == size;

    if (!legal)
      throw new InvalidDataException($"A CoCoMax picture is 6154, 6155, 6272 or 7168 bytes, got {data.Length}.");

    // Four lengths and no signature, so the header is most of the identification.
    if (data[0] != 0 || data[1] != 24 || data[2] > 1 || data[3] != 14 || data[4] != 0)
      throw new InvalidDataException("Not a CoCoMax picture: the header does not match.");

    var rawData = new byte[CoCoMaxFile.ExpectedFileSize];
    data.Slice(0, CoCoMaxFile.ExpectedFileSize).CopyTo(rawData);

    return new CoCoMaxFile { RawData = rawData };
    }

  public static CoCoMaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var legal = false;
    foreach (var size in CoCoMaxFile.LegalSizes)
      legal |= data.Length == size;

    if (!legal)
      throw new InvalidDataException($"A CoCoMax picture is 6154, 6155, 6272 or 7168 bytes, got {data.Length}.");

    // Four lengths and no signature, so the header is most of the identification.
    if (data[0] != 0 || data[1] != 24 || data[2] > 1 || data[3] != 14 || data[4] != 0)
      throw new InvalidDataException("Not a CoCoMax picture: the header does not match.");

    var rawData = new byte[CoCoMaxFile.ExpectedFileSize];
    data.AsSpan(0, CoCoMaxFile.ExpectedFileSize).CopyTo(rawData);

    return new CoCoMaxFile { RawData = rawData };
  }
}
