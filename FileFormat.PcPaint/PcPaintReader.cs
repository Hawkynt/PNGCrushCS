using System;
using System.IO;

namespace FileFormat.PcPaint;

/// <summary>Reads PC Paint/Pictor Page Format files from bytes, streams, or file paths.</summary>
public static class PcPaintReader {

  public static PcPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PC Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcPaintFile FromStream(Stream stream) {
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

  public static PcPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PcPaintFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid PC Paint file.");

    var magic = (ushort)(data[0] | (data[1] << 8));
    if (magic != PcPaintFile.Magic)
      throw new InvalidDataException($"Invalid PC Paint magic bytes (expected 0x1234, got 0x{magic:X4}).");

    var width = (ushort)(data[2] | (data[3] << 8));
    var height = (ushort)(data[4] | (data[5] << 8));

    if (width == 0)
      throw new InvalidDataException("PC Paint width must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("PC Paint height must be greater than zero.");

    var xOffset = (ushort)(data[6] | (data[7] << 8));
    var yOffset = (ushort)(data[8] | (data[9] << 8));
    var planes = data[10];
    var bitsPerPixel = data[11];

    if (planes < 1 || planes > 4)
      throw new InvalidDataException($"PC Paint planes must be 1-4, got {planes}.");
    if (bitsPerPixel != 1 && bitsPerPixel != 2 && bitsPerPixel != 4 && bitsPerPixel != 8)
      throw new InvalidDataException($"PC Paint bits per pixel must be 1, 2, 4, or 8, got {bitsPerPixel}.");

    var xAspect = (ushort)(data[12] | (data[13] << 8));
    var yAspect = (ushort)(data[14] | (data[15] << 8));
    var paletteInfoLength = (ushort)(data[16] | (data[17] << 8));

    var offset = PcPaintFile.HeaderSize;

    var palette = new byte[PcPaintFile.PaletteSize];
    if (paletteInfoLength > 0) {
      var paletteBytes = Math.Min((int)paletteInfoLength, PcPaintFile.PaletteSize);
      if (data.Length >= offset + paletteBytes)
        data.AsSpan(offset, paletteBytes).CopyTo(palette.AsSpan(0));

      offset += paletteInfoLength;
    }

    var pixelCount = width * height;
    var pixelData = _DecompressRle(data, offset, pixelCount);

    return new() {
      Width = width,
      Height = height,
      XOffset = xOffset,
      YOffset = yOffset,
      Planes = planes,
      BitsPerPixel = bitsPerPixel,
      XAspect = xAspect,
      YAspect = yAspect,
      Palette = palette,
      PixelData = pixelData,
    };
  }

  private static byte[] _DecompressRle(byte[] data, int offset, int expectedSize) {
    var output = new byte[expectedSize];
    var inIdx = offset;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      if (inIdx + 1 >= data.Length)
        break;

      var count = (int)data[inIdx++];
      var value = data[inIdx++];

      if (count == 0) {
        if (inIdx + 1 >= data.Length)
          break;

        count = data[inIdx] | (data[inIdx + 1] << 8);
        inIdx += 2;
      }

      for (var i = 0; i < count && outIdx < expectedSize; ++i)
        output[outIdx++] = value;
    }

    return output;
  }
}
