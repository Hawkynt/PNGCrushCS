using System;
using System.IO;

namespace FileFormat.C128Hires;

/// <summary>Reads C128 hires 320x200 mono bitmaps from bytes, streams, or file paths.</summary>
public static class C128HiresReader {

  public static C128HiresFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C128 hires file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C128HiresFile FromStream(Stream stream) {
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

  public static C128HiresFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static C128HiresFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != C128HiresFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid C128 hires data size: expected exactly {C128HiresFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    data.AsSpan(0, C128HiresFile.ExpectedFileSize).CopyTo(rawData);

    return new C128HiresFile { RawData = rawData };
  }
}
