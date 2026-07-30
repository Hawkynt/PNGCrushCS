using System;
using System.IO;

namespace FileFormat.PrintShopIcon;

/// <summary>Reads Print Shop graphics from bytes, streams, or file paths.</summary>
public static class PrintShopIconReader {

  public static PrintShopIconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Print Shop graphic not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintShopIconFile FromStream(Stream stream) {
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

  public static PrintShopIconFile FromSpan(ReadOnlySpan<byte> data) {
    // The bitmap is a fixed size; files vary only in what they carry after it.
    if (data.Length < PrintShopIconFile.BitmapSize || data.Length > PrintShopIconFile.MaxFileSize)
      throw new InvalidDataException(
        $"A Print Shop graphic is between {PrintShopIconFile.BitmapSize} and {PrintShopIconFile.MaxFileSize} bytes, got {data.Length}.");

    var bitmap = new byte[PrintShopIconFile.BitmapSize];
    data[..PrintShopIconFile.BitmapSize].CopyTo(bitmap);

    return new() { BitmapData = bitmap };
  }

  public static PrintShopIconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
