using System;
using System.IO;

namespace FileFormat.HiresBitmap;

/// <summary>Reads Hires Bitmap (.hbm) files from bytes, streams, or file paths.</summary>
public static class HiresBitmapReader {

  public static HiresBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresBitmapFile FromStream(Stream stream) {
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

  public static HiresBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < HiresBitmapFile.MinFileSize)
      throw new InvalidDataException($"File too small for Hires Bitmap format (got {data.Length} bytes, need at least {HiresBitmapFile.MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += HiresBitmapFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[HiresBitmapFile.BitmapDataSize];
    data.AsSpan(offset, HiresBitmapFile.BitmapDataSize).CopyTo(bitmapData);
    offset += HiresBitmapFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[HiresBitmapFile.ScreenDataSize];
    data.AsSpan(offset, HiresBitmapFile.ScreenDataSize).CopyTo(screenData);
    offset += HiresBitmapFile.ScreenDataSize;

    // Trailing data
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      trailingData = new byte[data.Length - offset];
      data.AsSpan(offset).CopyTo(trailingData);
    }

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      TrailingData = trailingData,
    };
  }
}
