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

  public static Trs80File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Trs80File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Trs80File.FileSize)
      throw new InvalidDataException($"Invalid TRS-80 data size: expected exactly {Trs80File.FileSize} bytes, got {data.Length}.");

    var rawData = new byte[Trs80File.FileSize];
    data.AsSpan(0, Trs80File.FileSize).CopyTo(rawData);

    return new Trs80File { RawData = rawData };
  }
}
