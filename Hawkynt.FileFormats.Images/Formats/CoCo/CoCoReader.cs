using System;
using System.IO;

namespace FileFormat.CoCo;

/// <summary>Reads TRS-80 CoCo PMODE 4 graphics screens from bytes, streams, or file paths.</summary>
public static class CoCoReader {

  public static CoCoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CoCo file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CoCoFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static CoCoFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != CoCoFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CoCo data size: expected exactly {CoCoFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CoCoFile.ExpectedFileSize];
    data.Slice(0, CoCoFile.ExpectedFileSize).CopyTo(rawData);

    return new CoCoFile { RawData = rawData };
    }

  public static CoCoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CoCoFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CoCo data size: expected exactly {CoCoFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CoCoFile.ExpectedFileSize];
    data.AsSpan(0, CoCoFile.ExpectedFileSize).CopyTo(rawData);

    return new CoCoFile { RawData = rawData };
  }
}
