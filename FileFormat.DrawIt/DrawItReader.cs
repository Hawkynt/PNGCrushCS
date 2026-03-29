using System;
using System.IO;

namespace FileFormat.DrawIt;

/// <summary>Reads DrawIt (DIT) files from bytes, streams, or file paths.</summary>
public static class DrawItReader {

  public static DrawItFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DrawIt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DrawItFile FromStream(Stream stream) {
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

  public static DrawItFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DrawItHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid DrawIt file.");

    var header = DrawItHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0)
      throw new InvalidDataException("Invalid DrawIt width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid DrawIt height: must be greater than zero.");

    var expectedSize = DrawItHeader.StructSize + DrawItFile.PaletteDataSize + width * height;
    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for DrawIt file: expected {expectedSize} bytes, got {data.Length}.");

    var palette = new byte[DrawItFile.PaletteDataSize];
    data.AsSpan(DrawItHeader.StructSize, DrawItFile.PaletteDataSize).CopyTo(palette.AsSpan(0));

    var pixelDataOffset = DrawItHeader.StructSize + DrawItFile.PaletteDataSize;
    var pixelCount = width * height;
    var pixelData = new byte[pixelCount];
    data.AsSpan(pixelDataOffset, pixelCount).CopyTo(pixelData.AsSpan(0));

    return new DrawItFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData
    };
  }
}
