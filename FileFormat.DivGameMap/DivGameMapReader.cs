using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DivGameMap;

/// <summary>Reads DIV Games Studio FPG files from bytes, streams, or file paths.</summary>
public static class DivGameMapReader {

  public static DivGameMapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FPG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DivGameMapFile FromStream(Stream stream) {
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

  public static DivGameMapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < DivGameMapFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid FPG file (need at least {DivGameMapFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != DivGameMapFile.Magic[0] || data[1] != DivGameMapFile.Magic[1] || data[2] != DivGameMapFile.Magic[2] || data[3] != DivGameMapFile.Magic[3])
      throw new InvalidDataException("Invalid FPG magic bytes.");

    var offset = DivGameMapFile.MagicSize;

    var palette = new byte[DivGameMapFile.PaletteSize];
    data.Slice(offset, DivGameMapFile.PaletteSize).CopyTo(palette);
    offset += DivGameMapFile.PaletteSize;

    if (data.Length < offset + DivGameMapFile.EntryHeaderSize)
      throw new InvalidDataException("FPG file has no entries.");

    // Parse first entry header: code(4) + length(4) + description(32) + filename(12) + width(4) + height(4) + numPoints(4)
    offset += 4; // skip code
    offset += 4; // skip length
    offset += 32; // skip description
    offset += 12; // skip filename

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    offset += 4;
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    offset += 4;
    var numPoints = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    offset += 4;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid FPG entry dimensions: {width}x{height}.");

    // Skip control points
    offset += numPoints * 4;

    var pixelCount = width * height;
    if (data.Length < offset + pixelCount)
      throw new InvalidDataException("FPG file truncated: not enough pixel data.");

    var pixelData = new byte[pixelCount];
    data.Slice(offset, pixelCount).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Palette = palette,
    };
  }

  public static DivGameMapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
