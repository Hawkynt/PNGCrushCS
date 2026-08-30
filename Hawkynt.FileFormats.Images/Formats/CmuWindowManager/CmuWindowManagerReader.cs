using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.CmuWindowManager;

/// <summary>Reads Carnegie Mellon University window-manager bitmap files.</summary>
public static class CmuWindowManagerReader {

  private const int _HeaderSize = 14;
  private const uint _Magic = 0xF10040BB;

  public static CmuWindowManagerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CMU window-manager bitmap not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CmuWindowManagerFile FromStream(Stream stream) {
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

  public static CmuWindowManagerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CmuWindowManagerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HeaderSize)
      throw new InvalidDataException("Truncated CMU window-manager bitmap header.");

    if (BinaryPrimitives.ReadUInt32BigEndian(data) != _Magic)
      throw new InvalidDataException("Invalid CMU window-manager bitmap magic.");

    var width = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
    var depth = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("CMU window-manager bitmap dimensions must be positive.");
    if ((long)width * height > CmuWindowManagerFile.MaximumPixels)
      throw new InvalidDataException($"CMU window-manager bitmap exceeds the {CmuWindowManagerFile.MaximumPixels:N0}-pixel implementation safety limit.");
    if (depth != 1)
      throw new InvalidDataException($"CMU window-manager bitmap depth must be 1, got {depth}.");

    var rasterLength = checked(CmuWindowManagerFile.GetRowStride(width) * height);
    var available = data.Length - _HeaderSize;
    if (available < rasterLength)
      throw new InvalidDataException($"Truncated CMU window-manager raster: expected {rasterLength} bytes, got {available}.");
    if (available > rasterLength)
      throw new InvalidDataException($"Unexpected trailing CMU window-manager data: expected {rasterLength} raster bytes, got {available}.");

    return new CmuWindowManagerFile {
      Width = width,
      Height = height,
      Depth = depth,
      RasterData = data[_HeaderSize..].ToArray(),
    };
  }
}
