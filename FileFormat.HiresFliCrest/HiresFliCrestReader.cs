using System;
using System.IO;

namespace FileFormat.HiresFliCrest;

/// <summary>Reads Hires FLI by Crest (.hfc) files from bytes, streams, or file paths.</summary>
public static class HiresFliCrestReader {

  public static HiresFliCrestFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires FLI Crest file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresFliCrestFile FromStream(Stream stream) {
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

  public static HiresFliCrestFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiresFliCrestFile.LoadAddressSize + HiresFliCrestFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hires FLI Crest file (expected at least {HiresFliCrestFile.LoadAddressSize + HiresFliCrestFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiresFliCrestFile.LoadAddressSize];
    data.AsSpan(HiresFliCrestFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
