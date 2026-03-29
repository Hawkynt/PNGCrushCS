using System;
using System.IO;

namespace FileFormat.Fli64;

/// <summary>Reads FLI Designer (FLI multicolor) image files from bytes, streams, or file paths.</summary>
public static class Fli64Reader {

  public static Fli64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Designer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Fli64File FromStream(Stream stream) {
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

  public static Fli64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < Fli64File.ExpectedFileSize)
      throw new InvalidDataException($"FLI Designer file too small (got {data.Length} bytes, expected {Fli64File.ExpectedFileSize}).");

    if (data.Length > Fli64File.ExpectedFileSize)
      throw new InvalidDataException($"FLI Designer file size mismatch (got {data.Length} bytes, expected {Fli64File.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Fli64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[Fli64File.BitmapDataSize];
    data.AsSpan(offset, Fli64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Fli64File.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    var screenData = new byte[Fli64File.ScreenDataSize];
    data.AsSpan(offset, Fli64File.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += Fli64File.ScreenDataSize;

    // Color RAM (1000 bytes)
    var colorRam = new byte[Fli64File.ColorRamSize];
    data.AsSpan(offset, Fli64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += Fli64File.ColorRamSize;

    // Padding (472 bytes)
    var padding = new byte[Fli64File.PaddingSize];
    data.AsSpan(offset, Fli64File.PaddingSize).CopyTo(padding.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorRam = colorRam,
      Padding = padding,
    };
  }
}
