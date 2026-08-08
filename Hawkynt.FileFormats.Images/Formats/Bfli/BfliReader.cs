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

  public static BfliFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BfliFile.LoadAddressSize + BfliFile.MinBitmapSize)
      throw new InvalidDataException($"Data too small for a valid BFLI file (expected at least {BfliFile.LoadAddressSize + BfliFile.MinBitmapSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    // The address the file loads at, which is the only thing in it that says what it is. Without
    // this any file longer than nine thousand bytes was accepted and drawn: handed a fax it
    // reported a 320 by 200 picture, confidently and wrongly. Every sample carries 0x3BFF, and the
    // address this format is documented to load at is 0x3C00, so both are taken and nothing else.
    if (loadAddress is not (BfliFile.SampleLoadAddress or BfliFile.DefaultLoadAddress))
      throw new InvalidDataException(
        $"Not a BFLI picture: it loads at ${loadAddress:X4}, and one loads at ${BfliFile.SampleLoadAddress:X4} or ${BfliFile.DefaultLoadAddress:X4}.");

    var rawData = new byte[data.Length - BfliFile.LoadAddressSize];
    data.Slice(BfliFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static BfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
