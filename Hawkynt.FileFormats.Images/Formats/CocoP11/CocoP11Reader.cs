using System;
using System.IO;

namespace FileFormat.CocoP11;

/// <summary>Reads Color Computer P11 pictures from bytes, streams, or file paths.</summary>
public static class CocoP11Reader {

  public static CocoP11File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CocoP11File FromStream(Stream stream) {
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

  public static CocoP11File FromSpan(ReadOnlySpan<byte> data) {
    if ((data.Length != CocoP11File.FileSize && data.Length != CocoP11File.LongFileSize)
        || data[0] != 0 || data[1] != 12 || data[3] != 14 || data[4] != 0)
      throw new InvalidDataException($"Not a P11 picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static CocoP11File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
