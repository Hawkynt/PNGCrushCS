using System;
using System.IO;

namespace FileFormat.C128;

/// <summary>Reads Commodore 128 VDC screen files from bytes, streams, or file paths.</summary>
public static class C128Reader {

  public static C128File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C128 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static C128File FromStream(Stream stream) {
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

  public static C128File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static C128File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != C128File.FileSize)
      throw new InvalidDataException($"Invalid C128 data size: expected exactly {C128File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[C128File.FileSize];
    data.AsSpan(0, C128File.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
