using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.SpeccyExtended;

/// <summary>Reads Speccy eXtended Graphics (SXG) pictures from bytes, streams, or file paths.</summary>
public static class SpeccyExtendedReader {

  /// <summary>What a file opens with: a byte, then the three letters.</summary>
  internal static ReadOnlySpan<byte> Magic => [0x7F, 0x53, 0x58, 0x47];

  public static SpeccyExtendedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SXG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpeccyExtendedFile FromStream(Stream stream) {
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

  public static SpeccyExtendedFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SpeccyExtendedFile.PixelOffset)
      throw new InvalidDataException($"Data too small for an SXG picture: {data.Length} bytes.");

    if (!data[..Magic.Length].SequenceEqual(Magic))
      throw new InvalidDataException("Not an SXG picture: it does not open with 0x7F and \"SXG\".");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[SpeccyExtendedFile.WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(SpeccyExtendedFile.WidthOffset + 2)..]);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An SXG picture of {width}x{height} is no size.");

    var needed = SpeccyExtendedFile.PixelOffset + (width * height + 1) / 2;
    if (data.Length < needed)
      throw new InvalidDataException($"An SXG picture of {width}x{height} takes {needed} bytes; this file is {data.Length}.");

    var palette = new byte[SpeccyExtendedFile.PaletteCount * 3];
    for (var i = 0; i < SpeccyExtendedFile.PaletteCount; ++i) {
      var value = BinaryPrimitives.ReadUInt16LittleEndian(data[(SpeccyExtendedFile.PaletteOffset + i * 2)..]);
      var (red, green, blue) = SpeccyExtendedFile.DecodeColor(value);
      palette[i * 3] = red;
      palette[i * 3 + 1] = green;
      palette[i * 3 + 2] = blue;
    }

    // Four bits a pixel, the high nibble first.
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i) {
      var b = data[SpeccyExtendedFile.PixelOffset + i / 2];
      pixels[i] = (byte)(i % 2 == 0 ? b >> 4 : b & 0x0F);
    }

    return new() { Width = width, Height = height, Palette = palette, PixelData = pixels };
  }

  public static SpeccyExtendedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
