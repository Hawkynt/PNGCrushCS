using System;
using System.IO;

namespace FileFormat.PrismPaint;

/// <summary>Reads Atari Falcon Prism Paint images from bytes, streams, or file paths.</summary>
public static class PrismPaintReader {

  public static PrismPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Prism Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrismPaintFile FromStream(Stream stream) {
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

  public static PrismPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PrismPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Prism Paint file (minimum {PrismPaintFile.MinFileSize} bytes, got {data.Length}).");

    // Read dimensions (LE u16)
    var width = (ushort)(data[0] | (data[1] << 8));
    var height = (ushort)(data[2] | (data[3] << 8));

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Prism Paint dimensions: {width}x{height}.");

    // Convert Falcon palette to RGB
    var rgbPalette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    PrismPaintFile.ConvertFalconPaletteToRgb(
      data.Slice(PrismPaintFile.HeaderSize, PrismPaintFile.PaletteDataSize),
      rgbPalette
    );

    // Read pixel data
    var pixelOffset = PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize;
    var expectedPixelBytes = width * height;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(pixelOffset, copyLen).CopyTo(pixelData);

    return new PrismPaintFile {
      Width = width,
      Height = height,
      Palette = rgbPalette,
      PixelData = pixelData,
    };
    }

  public static PrismPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PrismPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Prism Paint file (minimum {PrismPaintFile.MinFileSize} bytes, got {data.Length}).");

    // Read dimensions (LE u16)
    var width = (ushort)(data[0] | (data[1] << 8));
    var height = (ushort)(data[2] | (data[3] << 8));

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Prism Paint dimensions: {width}x{height}.");

    // Convert Falcon palette to RGB
    var rgbPalette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    PrismPaintFile.ConvertFalconPaletteToRgb(
      data.AsSpan(PrismPaintFile.HeaderSize, PrismPaintFile.PaletteDataSize),
      rgbPalette
    );

    // Read pixel data
    var pixelOffset = PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize;
    var expectedPixelBytes = width * height;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(pixelOffset, copyLen).CopyTo(pixelData);

    return new PrismPaintFile {
      Width = width,
      Height = height,
      Palette = rgbPalette,
      PixelData = pixelData,
    };
  }
}
