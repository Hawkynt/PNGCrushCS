using System;
using System.IO;

namespace FileFormat.MicroPainter8;

/// <summary>Reads Micro Painter (Atari 8-bit) images from bytes, streams, or file paths.</summary>
public static class MicroPainter8Reader {

  public static MicroPainter8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Micro Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroPainter8File FromStream(Stream stream) {
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

  public static MicroPainter8File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MicroPainter8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MicroPainter8File.FileSize)
      throw new InvalidDataException($"Invalid Micro Painter data size: expected exactly {MicroPainter8File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[MicroPainter8File.FileSize];
    data.AsSpan(0, MicroPainter8File.FileSize).CopyTo(pixelData);

    return new MicroPainter8File { PixelData = pixelData };
  }
}
