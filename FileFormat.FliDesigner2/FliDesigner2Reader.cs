using System;
using System.IO;

namespace FileFormat.FliDesigner2;

/// <summary>Reads FLI Designer 2 (enhanced FLI multicolor) image files from bytes, streams, or file paths.</summary>
public static class FliDesigner2Reader {

  public static FliDesigner2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Designer 2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliDesigner2File FromStream(Stream stream) {
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

  public static FliDesigner2File FromSpan(ReadOnlySpan<byte> data) {


    if (data.Length < FliDesigner2File.MinFileSize)
      throw new InvalidDataException($"FLI Designer 2 file too small (got {data.Length} bytes, minimum {FliDesigner2File.MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FliDesigner2File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[FliDesigner2File.BitmapDataSize];
    data.Slice(offset, FliDesigner2File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FliDesigner2File.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    var screenData = new byte[FliDesigner2File.ScreenDataSize];
    data.Slice(offset, FliDesigner2File.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += FliDesigner2File.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[FliDesigner2File.ColorRamSize];
    data.Slice(offset, FliDesigner2File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += FliDesigner2File.ColorRamSize;

    // Remaining data (472 base padding + any extra data)
    var remainingLength = data.Length - offset;
    var extraData = new byte[remainingLength];
    if (remainingLength > 0)
      data.Slice(offset, remainingLength).CopyTo(extraData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorRam = colorRam,
      ExtraData = extraData,
    };
    }

  public static FliDesigner2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < FliDesigner2File.MinFileSize)
      throw new InvalidDataException($"FLI Designer 2 file too small (got {data.Length} bytes, minimum {FliDesigner2File.MinFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FliDesigner2File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[FliDesigner2File.BitmapDataSize];
    data.AsSpan(offset, FliDesigner2File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FliDesigner2File.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    var screenData = new byte[FliDesigner2File.ScreenDataSize];
    data.AsSpan(offset, FliDesigner2File.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += FliDesigner2File.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[FliDesigner2File.ColorRamSize];
    data.AsSpan(offset, FliDesigner2File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += FliDesigner2File.ColorRamSize;

    // Remaining data (472 base padding + any extra data)
    var remainingLength = data.Length - offset;
    var extraData = new byte[remainingLength];
    if (remainingLength > 0)
      data.AsSpan(offset, remainingLength).CopyTo(extraData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorRam = colorRam,
      ExtraData = extraData,
    };
  }
}
