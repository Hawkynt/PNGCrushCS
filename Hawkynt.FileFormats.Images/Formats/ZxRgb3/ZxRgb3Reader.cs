using System;
using System.IO;

namespace FileFormat.ZxRgb3;

/// <summary>Reads ZX Spectrum RGB3 images from bytes, streams, or file paths.</summary>
public static class ZxRgb3Reader {

  public static ZxRgb3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RGB3 image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxRgb3File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static ZxRgb3File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ZxRgb3File.FileSize)
      throw new InvalidDataException($"An RGB3 image is {ZxRgb3File.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[ZxRgb3File.FileSize];
    data.CopyTo(bitmap);

    return new() { BitmapData = bitmap };
  }

  public static ZxRgb3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
