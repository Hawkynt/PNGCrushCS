using System;
using System.IO;

namespace FileFormat.RockyInterlace;

/// <summary>Reads Rocky Interlace (.rip) files from bytes, streams, or file paths.</summary>
public static class RockyInterlaceReader {

  public static RockyInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Rocky Interlace file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RockyInterlaceFile FromStream(Stream stream) {
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

  public static RockyInterlaceFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static RockyInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RockyInterlaceFile.LoadAddressSize + RockyInterlaceFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Rocky Interlace file (expected at least {RockyInterlaceFile.LoadAddressSize + RockyInterlaceFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - RockyInterlaceFile.LoadAddressSize];
    data.AsSpan(RockyInterlaceFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
