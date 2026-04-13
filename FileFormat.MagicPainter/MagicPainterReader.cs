using System;
using System.IO;

namespace FileFormat.MagicPainter;

/// <summary>Reads Magic Painter (MGP) files from bytes, streams, or file paths.</summary>
public static class MagicPainterReader {

  /// <summary>Minimum header size: 2 (width) + 2 (height) + 2 (palette count) = 6 bytes.</summary>
  internal const int HeaderSize = 6;

  /// <summary>Maximum number of palette entries.</summary>
  internal const int MaxPaletteEntries = 256;

  public static MagicPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Magic Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MagicPainterFile FromStream(Stream stream) {
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

  public static MagicPainterFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HeaderSize)
      throw new InvalidDataException($"Magic Painter file must be at least {HeaderSize} bytes, got {data.Length}.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    var paletteCount = data[4] | (data[5] << 8);

    if (width == 0)
      throw new InvalidDataException("Invalid MGP width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid MGP height: must be greater than zero.");
    if (paletteCount == 0 || paletteCount > MaxPaletteEntries)
      throw new InvalidDataException($"Invalid MGP palette count: must be 1..{MaxPaletteEntries}, got {paletteCount}.");

    var paletteSize = paletteCount * 3;
    var pixelDataSize = width * height;
    var expectedSize = HeaderSize + paletteSize + pixelDataSize;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Magic Painter file too small: expected {expectedSize} bytes, got {data.Length}.");

    var palette = new byte[paletteSize];
    data.Slice(HeaderSize, paletteSize).CopyTo(palette);

    var pixelData = new byte[pixelDataSize];
    data.Slice(HeaderSize + paletteSize, pixelDataSize).CopyTo(pixelData);

    return new MagicPainterFile {
      Width = width,
      Height = height,
      Palette = palette,
      PaletteCount = paletteCount,
      PixelData = pixelData,
    };
    }

  public static MagicPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HeaderSize)
      throw new InvalidDataException($"Magic Painter file must be at least {HeaderSize} bytes, got {data.Length}.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    var paletteCount = data[4] | (data[5] << 8);

    if (width == 0)
      throw new InvalidDataException("Invalid MGP width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid MGP height: must be greater than zero.");
    if (paletteCount == 0 || paletteCount > MaxPaletteEntries)
      throw new InvalidDataException($"Invalid MGP palette count: must be 1..{MaxPaletteEntries}, got {paletteCount}.");

    var paletteSize = paletteCount * 3;
    var pixelDataSize = width * height;
    var expectedSize = HeaderSize + paletteSize + pixelDataSize;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Magic Painter file too small: expected {expectedSize} bytes, got {data.Length}.");

    var palette = new byte[paletteSize];
    data.AsSpan(HeaderSize, paletteSize).CopyTo(palette);

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(HeaderSize + paletteSize, pixelDataSize).CopyTo(pixelData);

    return new MagicPainterFile {
      Width = width,
      Height = height,
      Palette = palette,
      PaletteCount = paletteCount,
      PixelData = pixelData,
    };
  }
}
