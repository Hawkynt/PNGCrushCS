using System;
using System.IO;

namespace FileFormat.GephardHires;

/// <summary>Reads Gephard Hires (.ghg) files from bytes, streams, or file paths.</summary>
public static class GephardHiresReader {

  public static GephardHiresFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Gephard Hires file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GephardHiresFile FromStream(Stream stream) {
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

  public static GephardHiresFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GephardHiresFile.LoadAddressSize + GephardHiresFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Gephard Hires file (expected at least {GephardHiresFile.LoadAddressSize + GephardHiresFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - GephardHiresFile.LoadAddressSize];
    data.AsSpan(GephardHiresFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
