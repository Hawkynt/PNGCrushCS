using System;
using System.IO;

namespace FileFormat.Bfli;

/// <summary>Reads BFLI (.bfl/.bfli) files from bytes, streams, or file paths.</summary>
public static class BfliReader {

  public static BfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BfliFile FromStream(Stream stream) {
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

  public static BfliFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static BfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BfliFile.LoadAddressSize + BfliFile.MinBitmapSize)
      throw new InvalidDataException($"Data too small for a valid BFLI file (expected at least {BfliFile.LoadAddressSize + BfliFile.MinBitmapSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - BfliFile.LoadAddressSize];
    data.AsSpan(BfliFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
