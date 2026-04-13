using System;
using System.IO;

namespace FileFormat.CoCo3;

/// <summary>Reads CoCo 3 GIME 320x200x16 graphics screens from bytes, streams, or file paths.</summary>
public static class CoCo3Reader {

  public static CoCo3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CoCo 3 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CoCo3File FromStream(Stream stream) {
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

  public static CoCo3File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != CoCo3File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CoCo 3 data size: expected exactly {CoCo3File.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CoCo3File.ExpectedFileSize];
    data.Slice(0, CoCo3File.ExpectedFileSize).CopyTo(rawData);

    return new CoCo3File { RawData = rawData };
    }

  public static CoCo3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CoCo3File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CoCo 3 data size: expected exactly {CoCo3File.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CoCo3File.ExpectedFileSize];
    data.AsSpan(0, CoCo3File.ExpectedFileSize).CopyTo(rawData);

    return new CoCo3File { RawData = rawData };
  }
}
