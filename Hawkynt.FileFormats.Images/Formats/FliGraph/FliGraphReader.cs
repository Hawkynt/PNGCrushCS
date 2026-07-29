using System;
using System.IO;

namespace FileFormat.FliGraph;

/// <summary>Reads FLI Graph (FLI multicolor variant) image files from bytes, streams, or file paths.</summary>
public static class FliGraphReader {

  public static FliGraphFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Graph file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliGraphFile FromStream(Stream stream) {
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

  public static FliGraphFile FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < FliGraphFile.ExpectedFileSize)
      throw new InvalidDataException($"FLI Graph file too small (got {data.Length} bytes, expected {FliGraphFile.ExpectedFileSize}).");

    if (data.Length > FliGraphFile.ExpectedFileSize)
      throw new InvalidDataException($"FLI Graph file size mismatch (got {data.Length} bytes, expected {FliGraphFile.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FliGraphFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[FliGraphFile.BitmapDataSize];
    data.Slice(offset, FliGraphFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FliGraphFile.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    var screenData = new byte[FliGraphFile.ScreenDataSize];
    data.Slice(offset, FliGraphFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += FliGraphFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[FliGraphFile.ColorRamSize];
    data.Slice(offset, FliGraphFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += FliGraphFile.ColorRamSize;

    // Padding (472 bytes)
    var padding = new byte[FliGraphFile.PaddingSize];
    data.Slice(offset, FliGraphFile.PaddingSize).CopyTo(padding.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorRam = colorRam,
      Padding = padding,
    };
    }

  public static FliGraphFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < FliGraphFile.ExpectedFileSize)
      throw new InvalidDataException($"FLI Graph file too small (got {data.Length} bytes, expected {FliGraphFile.ExpectedFileSize}).");

    if (data.Length > FliGraphFile.ExpectedFileSize)
      throw new InvalidDataException($"FLI Graph file size mismatch (got {data.Length} bytes, expected {FliGraphFile.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FliGraphFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[FliGraphFile.BitmapDataSize];
    data.AsSpan(offset, FliGraphFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FliGraphFile.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    var screenData = new byte[FliGraphFile.ScreenDataSize];
    data.AsSpan(offset, FliGraphFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += FliGraphFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[FliGraphFile.ColorRamSize];
    data.AsSpan(offset, FliGraphFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += FliGraphFile.ColorRamSize;

    // Padding (472 bytes)
    var padding = new byte[FliGraphFile.PaddingSize];
    data.AsSpan(offset, FliGraphFile.PaddingSize).CopyTo(padding.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorRam = colorRam,
      Padding = padding,
    };
  }
}
