using System;
using System.IO;

namespace FileFormat.Ffli;

/// <summary>Reads Full FLI (.ffli) files from bytes, streams, or file paths.</summary>
public static class FfliReader {

  public static FfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FfliFile FromStream(Stream stream) {
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

  public static FfliFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FfliFile.LoadAddressSize + FfliFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid FFLI file (expected at least {FfliFile.LoadAddressSize + FfliFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FfliFile.LoadAddressSize];
    data.Slice(FfliFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static FfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FfliFile.LoadAddressSize + FfliFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid FFLI file (expected at least {FfliFile.LoadAddressSize + FfliFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FfliFile.LoadAddressSize];
    data.AsSpan(FfliFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
