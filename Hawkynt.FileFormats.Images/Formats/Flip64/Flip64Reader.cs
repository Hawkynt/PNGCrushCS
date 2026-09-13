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

  public static Flip64File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static Flip64File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Flip64File.LoadAddressSize + Flip64File.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Flip file (expected at least {Flip64File.LoadAddressSize + Flip64File.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - Flip64File.LoadAddressSize];
    data.Slice(Flip64File.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static Flip64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
