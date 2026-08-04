using System;
using System.IO;

namespace FileFormat.Blazing;

/// <summary>Reads Blazing Paddles hires files from bytes, streams, or file paths.</summary>
public static class BlazingReader {

  public static BlazingFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Blazing Paddles file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BlazingFile FromStream(Stream stream) {
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

  public static BlazingFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length == BlazingFile.MulticolorFileSize)
      return new() {
        LoadAddress = (ushort)(data[0] | (data[1] << 8)),
        BitmapData = data.Slice(BlazingFile.LoadAddressSize, BlazingFile.BitmapDataSize).ToArray(),
        ScreenData = data.Slice(BlazingFile.MulticolorScreenOffset, BlazingFile.ScreenDataSize).ToArray(),
        ColorData = data.Slice(BlazingFile.MulticolorColorOffset, BlazingFile.ScreenDataSize).ToArray(),
      };

    if (data.Length < BlazingFile.ExpectedFileSize)
      throw new InvalidDataException($"A Blazing Paddles picture is {BlazingFile.MulticolorFileSize} bytes in multicolour or {BlazingFile.ExpectedFileSize} in hires; this file is {data.Length}.");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += BlazingFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[BlazingFile.BitmapDataSize];
    data.Slice(offset, BlazingFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += BlazingFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[BlazingFile.ScreenDataSize];
    data.Slice(offset, BlazingFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
    };
    }

  public static BlazingFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
