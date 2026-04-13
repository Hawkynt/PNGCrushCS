using System;
using System.IO;

namespace FileFormat.Mcs;

/// <summary>Reads Mcs (.mcs) files from bytes, streams, or file paths.</summary>
public static class McsReader {

  public static McsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mcs file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static McsFile FromStream(Stream stream) {
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

  public static McsFile FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < McsFile.MinFileSize)
      throw new InvalidDataException($"File too small for Mcs format (got {data.Length} bytes, need at least {McsFile.MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += McsFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[McsFile.BitmapDataSize];
    data.Slice(offset, McsFile.BitmapDataSize).CopyTo(bitmapData);
    offset += McsFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[McsFile.ScreenDataSize];
    data.Slice(offset, McsFile.ScreenDataSize).CopyTo(screenData);
    offset += McsFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorData = new byte[McsFile.ColorDataSize];
    data.Slice(offset, McsFile.ColorDataSize).CopyTo(colorData);
    offset += McsFile.ColorDataSize;

    // Background color (1 byte)
    var backgroundColor = data[offset];
    ++offset;

    // Trailing data
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      trailingData = new byte[data.Length - offset];
      data[offset..].CopyTo(trailingData);
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

  public static McsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
