using System;
using System.IO;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>Reads Hires Interlace by Feniks (.hlf) files from bytes, streams, or file paths.</summary>
public static class HiresInterlaceFeniksReader {

  public static HiresInterlaceFeniksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Interlace Feniks file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresInterlaceFeniksFile FromStream(Stream stream) {
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

  public static HiresInterlaceFeniksFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HiresInterlaceFeniksFile.LoadAddressSize + HiresInterlaceFeniksFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hires Interlace Feniks file (expected at least {HiresInterlaceFeniksFile.LoadAddressSize + HiresInterlaceFeniksFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiresInterlaceFeniksFile.LoadAddressSize];
    data.Slice(HiresInterlaceFeniksFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static HiresInterlaceFeniksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiresInterlaceFeniksFile.LoadAddressSize + HiresInterlaceFeniksFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hires Interlace Feniks file (expected at least {HiresInterlaceFeniksFile.LoadAddressSize + HiresInterlaceFeniksFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiresInterlaceFeniksFile.LoadAddressSize];
    data.AsSpan(HiresInterlaceFeniksFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
