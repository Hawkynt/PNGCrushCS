using System;
using System.IO;

namespace FileFormat.CompW;

/// <summary>Reads CompW WLM files from bytes, streams, or file paths.</summary>
public static class CompWReader {

  public static CompWFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WLM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CompWFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static CompWFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CompWFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid WLM file (need at least {CompWFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != CompWFile.Magic[0] || data[1] != CompWFile.Magic[1])
      throw new InvalidDataException("Invalid WLM magic bytes.");

    var header = CompWHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;
    var bpp = header.Bpp;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid WLM dimensions: {width}x{height}.");

    var pixelCount = width * height;
    // Layout: header(8) + pixel data(w*h) + palette(768)
    if (data.Length < CompWFile.HeaderSize + pixelCount + CompWFile.PaletteSize)
      throw new InvalidDataException("WLM file truncated.");

    var pixelData = new byte[pixelCount];
    data.Slice(CompWFile.HeaderSize, pixelCount).CopyTo(pixelData);

    var palette = new byte[CompWFile.PaletteSize];
    data.Slice(CompWFile.HeaderSize + pixelCount, CompWFile.PaletteSize).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      PixelData = pixelData,
      Palette = palette,
    };
  }

  public static CompWFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
