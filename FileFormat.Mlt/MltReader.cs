using System;
using System.IO;

namespace FileFormat.Mlt;

/// <summary>Reads Mlt (.mlt) files from bytes, streams, or file paths.</summary>
public static class MltReader {

  public static MltFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mlt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MltFile FromStream(Stream stream) {
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

  public static MltFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < MltFile.MinFileSize)
      throw new InvalidDataException($"File too small for Mlt format (got {data.Length} bytes, need at least {MltFile.MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += MltFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[MltFile.BitmapDataSize];
    data.AsSpan(offset, MltFile.BitmapDataSize).CopyTo(bitmapData);
    offset += MltFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[MltFile.ScreenDataSize];
    data.AsSpan(offset, MltFile.ScreenDataSize).CopyTo(screenData);
    offset += MltFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[MltFile.ColorDataSize];
    data.AsSpan(offset, MltFile.ColorDataSize).CopyTo(colorData);
    offset += MltFile.ColorDataSize;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

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
      ColorData = colorData,
      BackgroundColor = backgroundColor,
      TrailingData = trailingData,
    };
  }
}
