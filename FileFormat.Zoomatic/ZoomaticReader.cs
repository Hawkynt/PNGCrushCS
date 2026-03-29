using System;
using System.IO;

namespace FileFormat.Zoomatic;

/// <summary>Reads Zoomatic (.zom) files from bytes, streams, or file paths.</summary>
public static class ZoomaticReader {

  public static ZoomaticFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zoomatic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZoomaticFile FromStream(Stream stream) {
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

  public static ZoomaticFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < ZoomaticFile.LoadAddressSize + ZoomaticFile.MinPayloadSize)
      throw new InvalidDataException($"File too small for Zoomatic format (got {data.Length} bytes, need at least {ZoomaticFile.LoadAddressSize + ZoomaticFile.MinPayloadSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += ZoomaticFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[ZoomaticFile.BitmapDataSize];
    data.AsSpan(offset, ZoomaticFile.BitmapDataSize).CopyTo(bitmapData);
    offset += ZoomaticFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[ZoomaticFile.ScreenDataSize];
    data.AsSpan(offset, ZoomaticFile.ScreenDataSize).CopyTo(screenData);
    offset += ZoomaticFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[ZoomaticFile.ColorDataSize];
    data.AsSpan(offset, ZoomaticFile.ColorDataSize).CopyTo(colorData);
    offset += ZoomaticFile.ColorDataSize;

    // Background color: first byte of trailing data if available, else 0
    byte backgroundColor = 0;
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      backgroundColor = data[offset];
      ++offset;
      if (offset < data.Length) {
        trailingData = new byte[data.Length - offset];
        data.AsSpan(offset).CopyTo(trailingData);
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
}
