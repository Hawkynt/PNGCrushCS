using System;
using System.IO;

namespace FileFormat.ShfXlEdit;

/// <summary>Reads SHF-XL Edit pictures from bytes, streams, or file paths.</summary>
public static class ShfXlEditReader {

  public static ShfXlEditFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ShfXlEditFile FromStream(Stream stream) {
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

  public static ShfXlEditFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length == ShfXlEditFile.RawFileSize)
      return new() { Data = data.ToArray(), IsRaw = true };

    if (data.Length < 6)
      throw new InvalidDataException($"Not an SHF-XL picture: {data.Length} bytes.");

    return new() { Data = _Unpack(data), IsRaw = false };
  }

  /// <summary>
  /// Unpacks the run-length encoding, which runs backwards from the end of the file.
  /// </summary>
  /// <remarks>
  /// The escape byte is the last byte of the file rather than part of a header, which follows from
  /// the direction: the first thing a backwards reader meets is the last thing written. The two
  /// bytes at the front are the load address and are never data.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var at = data.Length;
    var escape = _Previous(data, ref at);
    var unpacked = new byte[ShfXlEditFile.UnpackedSize];

    for (var target = ShfXlEditFile.UnpackedSize - 1; target >= 0;) {
      var value = _Previous(data, ref at);
      var count = 1;

      if (value == escape) {
        count = _Previous(data, ref at);
        if (count == 0)
          count = 256;

        value = _Previous(data, ref at);
      }

      while (count-- > 0 && target >= 0)
        unpacked[target--] = value;
    }

    return unpacked;
  }

  private static byte _Previous(ReadOnlySpan<byte> data, ref int at) {
    if (at <= 2)
      throw new InvalidDataException("An SHF-XL picture ends before its picture does.");

    return data[--at];
  }

  public static ShfXlEditFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
