using System;
using System.IO;

namespace FileFormat.HiresManager;

/// <summary>Reads Hires Manager by Cosmos (.him) files from bytes, streams, or file paths.</summary>
public static class HiresManagerReader {

  public static HiresManagerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Manager file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresManagerFile FromStream(Stream stream) {
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

  public static HiresManagerFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HiresManagerFile.LoadAddressSize + HiresManagerFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hires Manager file (expected at least {HiresManagerFile.LoadAddressSize + HiresManagerFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiresManagerFile.LoadAddressSize];
    data.Slice(HiresManagerFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static HiresManagerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiresManagerFile.LoadAddressSize + HiresManagerFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hires Manager file (expected at least {HiresManagerFile.LoadAddressSize + HiresManagerFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiresManagerFile.LoadAddressSize];
    data.AsSpan(HiresManagerFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
