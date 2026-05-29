using System;
using System.IO;

namespace FileFormat.HardInterlace;

/// <summary>Reads Hard Interlace (.hip) files from bytes, streams, or file paths.</summary>
public static class HardInterlaceReader {

  public static HardInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hard Interlace file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HardInterlaceFile FromStream(Stream stream) {
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

  public static HardInterlaceFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HardInterlaceFile.LoadAddressSize + HardInterlaceFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hard Interlace file (expected at least {HardInterlaceFile.LoadAddressSize + HardInterlaceFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HardInterlaceFile.LoadAddressSize];
    data.Slice(HardInterlaceFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static HardInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HardInterlaceFile.LoadAddressSize + HardInterlaceFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hard Interlace file (expected at least {HardInterlaceFile.LoadAddressSize + HardInterlaceFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HardInterlaceFile.LoadAddressSize];
    data.AsSpan(HardInterlaceFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
