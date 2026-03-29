using System;
using System.IO;

namespace FileFormat.Cel;

/// <summary>Reads KiSS CEL files from bytes, streams, or file paths.</summary>
public static class CelReader {

  private static readonly byte[] _Magic = [(byte)'K', (byte)'i', (byte)'S', (byte)'S'];

  public static CelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CEL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CelFile FromStream(Stream stream) {
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

  public static CelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CelHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid CEL file.");

    if (data[0] != _Magic[0] || data[1] != _Magic[1] || data[2] != _Magic[2] || data[3] != _Magic[3])
      throw new InvalidDataException($"Invalid CEL magic: expected 'KiSS', got 0x{data[0]:X2}{data[1]:X2}{data[2]:X2}{data[3]:X2}.");

    var header = CelHeader.ReadFrom(data.AsSpan());
    var mark = header.Mark;
    var bpp = header.BitsPerPixel;
    var width = (int)header.Width;
    var height = (int)header.Height;
    var xOffset = (int)header.XOffset;
    var yOffset = (int)header.YOffset;

    if (width == 0)
      throw new InvalidDataException("Invalid CEL width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid CEL height: must be greater than zero.");

    return mark switch {
      0x04 => _ReadIndexed(data, bpp, width, height, xOffset, yOffset),
      0x20 => _ReadRgba32(data, bpp, width, height, xOffset, yOffset),
      _ => throw new InvalidDataException($"Unsupported CEL mark byte: 0x{mark:X2}. Expected 0x04 (indexed) or 0x20 (RGBA32).")
    };
  }

  private static CelFile _ReadIndexed(byte[] data, byte bpp, int width, int height, int xOffset, int yOffset) {
    if (bpp is not (4 or 8))
      throw new InvalidDataException($"Invalid bits per pixel for indexed CEL: expected 4 or 8, got {bpp}.");

    int pixelDataSize;
    if (bpp == 8)
      pixelDataSize = width * height;
    else
      pixelDataSize = ((width + 1) / 2) * height;

    if (data.Length < CelHeader.StructSize + pixelDataSize)
      throw new InvalidDataException($"Data too small for pixel data: expected at least {CelHeader.StructSize + pixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(CelHeader.StructSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new CelFile {
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      XOffset = xOffset,
      YOffset = yOffset,
      PixelData = pixelData,
    };
  }

  private static CelFile _ReadRgba32(byte[] data, byte bpp, int width, int height, int xOffset, int yOffset) {
    if (bpp != 32)
      throw new InvalidDataException($"Invalid bits per pixel for RGBA32 CEL: expected 32, got {bpp}.");

    var pixelDataSize = width * height * 4;

    if (data.Length < CelHeader.StructSize + pixelDataSize)
      throw new InvalidDataException($"Data too small for pixel data: expected at least {CelHeader.StructSize + pixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(CelHeader.StructSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new CelFile {
      Width = width,
      Height = height,
      BitsPerPixel = 32,
      XOffset = xOffset,
      YOffset = yOffset,
      PixelData = pixelData,
    };
  }
}
