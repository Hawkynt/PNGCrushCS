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

  public static PaintShopFile FromStream(Stream stream) {
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
