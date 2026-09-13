using System;
using System.IO;

namespace FileFormat.PaintShop;

/// <summary>Reads PaintShop pages from bytes, streams, or file paths.</summary>
public static class PaintShopReader {

  public static PaintShopFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PaintShop page not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintShopFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static PaintShopFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != PaintShopFile.FileSize)
      throw new InvalidDataException($"A PaintShop page is {PaintShopFile.FileSize} bytes, got {data.Length}.");

    return new() { BitmapData = data.ToArray() };
  }

  public static PaintShopFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
