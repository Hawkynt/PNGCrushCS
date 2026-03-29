using System;
using System.IO;

namespace FileFormat.FliProfi;

/// <summary>Reads FLI Profi (.fpr) files from bytes, streams, or file paths.</summary>
public static class FliProfiReader {

  public static FliProfiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Profi file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliProfiFile FromStream(Stream stream) {
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

  public static FliProfiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid FLI Profi file (expected at least {FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FliProfiFile.LoadAddressSize];
    data.AsSpan(FliProfiFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
