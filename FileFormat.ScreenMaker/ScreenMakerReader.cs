using System;
using System.IO;

namespace FileFormat.ScreenMaker;

/// <summary>Reads Screen Maker files from bytes, streams, or file paths.</summary>
public static class ScreenMakerReader {

  public static ScreenMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen Maker file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ScreenMakerFile FromStream(Stream stream) {
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

  public static ScreenMakerFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize)
      throw new InvalidDataException($"Data too small for a valid Screen Maker file (minimum {ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize} bytes, got {data.Length}).");

    var width = (ushort)(data[0] | (data[1] << 8));
    var height = (ushort)(data[2] | (data[3] << 8));

    if (width == 0)
      throw new InvalidDataException("Invalid Screen Maker width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid Screen Maker height: 0.");

    var pixelCount = width * height;
    var expectedSize = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + pixelCount;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedSize} bytes, got {data.Length}.");

    var offset = ScreenMakerFile.HeaderSize;

    // Palette (768 bytes)
    var palette = new byte[ScreenMakerFile.PaletteDataSize];
    data.Slice(offset, ScreenMakerFile.PaletteDataSize).CopyTo(palette.AsSpan(0));
    offset += ScreenMakerFile.PaletteDataSize;

    // Pixel data (width x height bytes)
    var pixelData = new byte[pixelCount];
    data.Slice(offset, pixelCount).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static ScreenMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize)
      throw new InvalidDataException($"Data too small for a valid Screen Maker file (minimum {ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize} bytes, got {data.Length}).");

    var width = (ushort)(data[0] | (data[1] << 8));
    var height = (ushort)(data[2] | (data[3] << 8));

    if (width == 0)
      throw new InvalidDataException("Invalid Screen Maker width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid Screen Maker height: 0.");

    var pixelCount = width * height;
    var expectedSize = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + pixelCount;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedSize} bytes, got {data.Length}.");

    var offset = ScreenMakerFile.HeaderSize;

    // Palette (768 bytes)
    var palette = new byte[ScreenMakerFile.PaletteDataSize];
    data.AsSpan(offset, ScreenMakerFile.PaletteDataSize).CopyTo(palette.AsSpan(0));
    offset += ScreenMakerFile.PaletteDataSize;

    // Pixel data (width x height bytes)
    var pixelData = new byte[pixelCount];
    data.AsSpan(offset, pixelCount).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
