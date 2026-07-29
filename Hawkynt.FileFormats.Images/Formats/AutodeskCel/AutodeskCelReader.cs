using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AutodeskCel;

/// <summary>Reads Autodesk Animator CEL/PIC files from bytes, streams, or file paths.</summary>
public static class AutodeskCelReader {

  public static AutodeskCelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Autodesk CEL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AutodeskCelFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AutodeskCelFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AutodeskCelFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid Autodesk CEL file: expected at least {AutodeskCelFile.HeaderSize} bytes, got {data.Length}.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data);
    if (magic != AutodeskCelFile.Magic)
      throw new InvalidDataException($"Invalid Autodesk CEL magic: expected 0x{AutodeskCelFile.Magic:X4}, got 0x{magic:X4}.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var xOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var yOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var compression = data[12];

    if (width == 0)
      throw new InvalidDataException("Invalid Autodesk CEL width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid Autodesk CEL height: must be greater than zero.");
    if (bitsPerPixel != 8)
      throw new InvalidDataException($"Unsupported bits per pixel: expected 8, got {bitsPerPixel}.");
    if (compression != 0)
      throw new InvalidDataException($"Unsupported compression type: expected 0 (none), got {compression}.");

    var pixelDataSize = width * height;
    var expectedWithoutPalette = AutodeskCelFile.HeaderSize + pixelDataSize;
    var expectedWithPalette = expectedWithoutPalette + AutodeskCelFile.PaletteSize;

    if (data.Length < expectedWithoutPalette)
      throw new InvalidDataException($"Data too small for pixel data: expected at least {expectedWithoutPalette} bytes, got {data.Length}.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(AutodeskCelFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    var palette = _ReadPalette(data, expectedWithoutPalette);

    return new AutodeskCelFile {
      Width = width,
      Height = height,
      XOffset = xOffset,
      YOffset = yOffset,
      BitsPerPixel = bitsPerPixel,
      Compression = compression,
      PixelData = pixelData,
      Palette = palette,
    };
  }

  public static AutodeskCelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static byte[] _ReadPalette(ReadOnlySpan<byte> data, int expectedWithoutPalette) {
    var hasPalette = data.Length >= expectedWithoutPalette + AutodeskCelFile.PaletteSize;
    var palette = new byte[AutodeskCelFile.PaletteSize];

    if (hasPalette) {
      // 6-bit VGA palette values (0-63), multiply by 4 to get 8-bit (0-252)
      var paletteOffset = expectedWithoutPalette;
      for (var i = 0; i < AutodeskCelFile.PaletteSize; ++i)
        palette[i] = (byte)(data[paletteOffset + i] * 4);
    } else {
      // Default grayscale palette
      for (var i = 0; i < AutodeskCelFile.PaletteEntryCount; ++i) {
        var value = (byte)i;
        palette[i * 3] = value;
        palette[i * 3 + 1] = value;
        palette[i * 3 + 2] = value;
      }
    }

    return palette;
  }
}
