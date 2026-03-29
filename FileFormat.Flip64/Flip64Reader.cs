using System;
using System.IO;

namespace FileFormat.Flip64;

/// <summary>Reads Flip (.fbi) files from bytes, streams, or file paths.</summary>
public static class Flip64Reader {

  public static Flip64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Flip file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Flip64File FromStream(Stream stream) {
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

  public static Flip64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Flip64File.LoadAddressSize + Flip64File.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Flip file (expected at least {Flip64File.LoadAddressSize + Flip64File.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - Flip64File.LoadAddressSize];
    data.AsSpan(Flip64File.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
