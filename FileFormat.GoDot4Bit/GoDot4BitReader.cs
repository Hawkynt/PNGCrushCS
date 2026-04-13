using System;
using System.IO;

namespace FileFormat.GoDot4Bit;

/// <summary>Reads Commodore 64 GoDot 4-bit files from bytes, streams, or file paths.</summary>
public static class GoDot4BitReader {

  public static GoDot4BitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GoDot 4-bit file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GoDot4BitFile FromStream(Stream stream) {
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

  public static GoDot4BitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GoDot4BitFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < GoDot4BitFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid GoDot 4-bit file (expected {GoDot4BitFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != GoDot4BitFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid GoDot 4-bit file size (expected {GoDot4BitFile.ExpectedFileSize} bytes, got {data.Length}).");

    var pixelData = new byte[GoDot4BitFile.ExpectedFileSize];
    data.Slice(0, GoDot4BitFile.ExpectedFileSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      PixelData = pixelData,
    };
  }
}
