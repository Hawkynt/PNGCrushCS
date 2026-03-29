using System;
using System.IO;

namespace FileFormat.C16Plus4;

/// <summary>Reads Commodore 16/Plus4 multicolor screen files from bytes, streams, or file paths.</summary>
public static class C16Plus4Reader {

  public static C16Plus4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C16Plus4 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C16Plus4File FromStream(Stream stream) {
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

  public static C16Plus4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != C16Plus4File.FileSize)
      throw new InvalidDataException($"Invalid C16Plus4 data size: expected exactly {C16Plus4File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[C16Plus4File.FileSize];
    data.AsSpan(0, C16Plus4File.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
