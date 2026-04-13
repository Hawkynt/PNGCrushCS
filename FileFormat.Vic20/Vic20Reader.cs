using System;
using System.IO;

namespace FileFormat.Vic20;

/// <summary>Reads Commodore VIC-20 screen dump files from bytes, streams, or file paths.</summary>
public static class Vic20Reader {

  public static Vic20File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vic20 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Vic20File FromStream(Stream stream) {
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

  public static Vic20File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != Vic20File.FileSize)
      throw new InvalidDataException($"Invalid Vic20 data size: expected exactly {Vic20File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[Vic20File.FileSize];
    data.Slice(0, Vic20File.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static Vic20File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Vic20File.FileSize)
      throw new InvalidDataException($"Invalid Vic20 data size: expected exactly {Vic20File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[Vic20File.FileSize];
    data.AsSpan(0, Vic20File.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
