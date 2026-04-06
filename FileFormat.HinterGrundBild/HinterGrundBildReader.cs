using System;
using System.IO;

namespace FileFormat.HinterGrundBild;

/// <summary>Reads HinterGrundBild (.hgb) files from bytes, streams, or file paths.</summary>
public static class HinterGrundBildReader {

  public static HinterGrundBildFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HinterGrundBild file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HinterGrundBildFile FromStream(Stream stream) {
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

  public static HinterGrundBildFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static HinterGrundBildFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < HinterGrundBildFile.LoadAddressSize + HinterGrundBildFile.MinPayloadSize)
      throw new InvalidDataException($"File too small for HinterGrundBild format (got {data.Length} bytes, need at least {HinterGrundBildFile.LoadAddressSize + HinterGrundBildFile.MinPayloadSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += HinterGrundBildFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[HinterGrundBildFile.BitmapDataSize];
    data.AsSpan(offset, HinterGrundBildFile.BitmapDataSize).CopyTo(bitmapData);
    offset += HinterGrundBildFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[HinterGrundBildFile.ScreenDataSize];
    data.AsSpan(offset, HinterGrundBildFile.ScreenDataSize).CopyTo(screenData);
    offset += HinterGrundBildFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[HinterGrundBildFile.ColorDataSize];
    data.AsSpan(offset, HinterGrundBildFile.ColorDataSize).CopyTo(colorData);
    offset += HinterGrundBildFile.ColorDataSize;

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
