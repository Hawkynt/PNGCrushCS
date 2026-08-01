using System;
using System.IO;

namespace FileFormat.Trs80;

/// <summary>Reads TRS-80 hi-res graphics screen dumps from bytes, streams, or file paths.</summary>
public static class Trs80Reader {

  public static Trs80File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TRS-80 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Trs80File FromStream(Stream stream) {
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

  public static Trs80File FromSpan(ReadOnlySpan<byte> data) {

    // The bitmap is 19200 bytes; a saved screen may carry another 128 or 256 of the board's state
    // after it, which is not part of the picture and is simply left where it is.
    if (data.Length is not (Trs80File.FileSize or Trs80File.FileSize + 128 or Trs80File.FileSize + 256))
      throw new InvalidDataException($"Invalid TRS-80 data size: expected {Trs80File.FileSize} bytes, got {data.Length}.");

    return new() { RawData = data[..Trs80File.FileSize].ToArray() };
  }

  public static Trs80File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
