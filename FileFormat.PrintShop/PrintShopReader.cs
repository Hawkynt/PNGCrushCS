using System;
using System.IO;

namespace FileFormat.PrintShop;

/// <summary>Reads Print Shop graphics (PSA/PSB) from bytes, streams, or file paths.</summary>
public static class PrintShopReader {

  public static PrintShopFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Print Shop file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintShopFile FromStream(Stream stream) {
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

  public static PrintShopFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length == PrintShopFile.PsbFileSize) {
      var pixelData = new byte[PrintShopFile.PixelDataSize];
      data.AsSpan(PrintShopFile.PsbHeaderSize, PrintShopFile.PixelDataSize).CopyTo(pixelData.AsSpan(0));
      return new() { PixelData = pixelData, IsFormatB = true };
    }

    if (data.Length == PrintShopFile.PsaFileSize) {
      var pixelData = new byte[PrintShopFile.PixelDataSize];
      data.AsSpan(0, PrintShopFile.PixelDataSize).CopyTo(pixelData);
      return new() { PixelData = pixelData, IsFormatB = false };
    }

    throw new InvalidDataException($"Invalid Print Shop data size: expected {PrintShopFile.PsaFileSize} (PSA) or {PrintShopFile.PsbFileSize} (PSB) bytes, got {data.Length}.");
  }
}
