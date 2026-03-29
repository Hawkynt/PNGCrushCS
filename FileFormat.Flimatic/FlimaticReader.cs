using System;
using System.IO;

namespace FileFormat.Flimatic;

/// <summary>Reads Commodore 64 Flimatic (.flm) files from bytes, streams, or file paths.</summary>
public static class FlimaticReader {

  public static FlimaticFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Flimatic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FlimaticFile FromStream(Stream stream) {
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

  public static FlimaticFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FlimaticFile.LoadAddressSize + FlimaticFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Flimatic file (expected at least {FlimaticFile.LoadAddressSize + FlimaticFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FlimaticFile.LoadAddressSize];
    data.AsSpan(FlimaticFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
