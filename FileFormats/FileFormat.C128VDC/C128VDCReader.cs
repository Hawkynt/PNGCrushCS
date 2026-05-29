using System;
using System.IO;

namespace FileFormat.C128VDC;

/// <summary>Reads C128 VDC 640x200 mono bitmaps from bytes, streams, or file paths.</summary>
public static class C128VDCReader {

  public static C128VDCFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C128 VDC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C128VDCFile FromStream(Stream stream) {
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

  public static C128VDCFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != C128VDCFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid C128 VDC data size: expected exactly {C128VDCFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    data.Slice(0, C128VDCFile.ExpectedFileSize).CopyTo(rawData);

    return new C128VDCFile { RawData = rawData };
    }

  public static C128VDCFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != C128VDCFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid C128 VDC data size: expected exactly {C128VDCFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    data.AsSpan(0, C128VDCFile.ExpectedFileSize).CopyTo(rawData);

    return new C128VDCFile { RawData = rawData };
  }
}
