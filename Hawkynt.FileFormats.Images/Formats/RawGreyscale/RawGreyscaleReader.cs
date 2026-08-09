using System;
using System.IO;

namespace FileFormat.RawGreyscale;

/// <summary>Reads raw greyscale dumps, whose whole content is their pixels.</summary>
public static class RawGreyscaleReader {

  public static RawGreyscaleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dump not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RawGreyscaleFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static RawGreyscaleFile FromSpan(ReadOnlySpan<byte> data) {
    var (width, height) = RawGreyscaleFile.SizeOf(data.Length);

    return new() { Width = width, Height = height, PixelData = data.ToArray() };
  }

  public static RawGreyscaleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
