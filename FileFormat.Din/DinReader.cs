using System;
using System.IO;

namespace FileFormat.Din;

/// <summary>Reads Din (.din) files from bytes, streams, or file paths.</summary>
public static class DinReader {

  public static DinFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Din file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DinFile FromStream(Stream stream) {
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

  public static DinFile FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < DinFile.LoadAddressSize + DinFile.MinPayloadSize)
      throw new InvalidDataException($"File too small for Din format (got {data.Length} bytes, need at least {DinFile.LoadAddressSize + DinFile.MinPayloadSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += DinFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[DinFile.BitmapDataSize];
    data.Slice(offset, DinFile.BitmapDataSize).CopyTo(bitmapData);
    offset += DinFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[DinFile.ScreenDataSize];
    data.Slice(offset, DinFile.ScreenDataSize).CopyTo(screenData);
    offset += DinFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[DinFile.ColorDataSize];
    data.Slice(offset, DinFile.ColorDataSize).CopyTo(colorData);
    offset += DinFile.ColorDataSize;

    // Background color: first byte of trailing data if available, else 0
    byte backgroundColor = 0;
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      backgroundColor = data[offset];
      ++offset;
      if (offset < data.Length) {
        trailingData = new byte[data.Length - offset];
        data[offset..].CopyTo(trailingData);
      }
    }

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = backgroundColor,
      TrailingData = trailingData,
    };
  }

  public static DinFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
