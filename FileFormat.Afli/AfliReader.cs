using System;
using System.IO;

namespace FileFormat.Afli;

/// <summary>Reads AFLI (Advanced FLI) hires image files from bytes, streams, or file paths.</summary>
public static class AfliReader {

  public static AfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AfliFile FromStream(Stream stream) {
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

  public static AfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < AfliFile.ExpectedFileSize)
      throw new InvalidDataException($"AFLI file too small (got {data.Length} bytes, expected {AfliFile.ExpectedFileSize}).");

    if (data.Length > AfliFile.ExpectedFileSize)
      throw new InvalidDataException($"AFLI file size mismatch (got {data.Length} bytes, expected {AfliFile.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += AfliFile.LoadAddressSize;

    // Raw FLI data (9216 bytes)
    var rawData = new byte[AfliFile.RawDataSize];
    data.AsSpan(offset, AfliFile.RawDataSize).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
