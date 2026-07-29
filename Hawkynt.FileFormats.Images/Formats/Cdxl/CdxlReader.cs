using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Cdxl;

public static class CdxlReader {

  private const int _HEADER_SIZE = 32;

  public static CdxlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CDXL file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CdxlFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static CdxlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CdxlFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("CDXL data shorter than the 32-byte chunk header.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data[20..22]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[22..24]);
    var planes = BinaryPrimitives.ReadUInt16BigEndian(data[24..26]);
    var paletteSize = BinaryPrimitives.ReadUInt16BigEndian(data[26..28]);
    var audioSize = BinaryPrimitives.ReadUInt16BigEndian(data[28..30]);
    if (width is 0 or > 4096 || height is 0 or > 4096 || planes is 0 or > 8)
      throw new InvalidDataException($"CDXL header reports implausible geometry: {width}x{height}x{planes}p.");
    if ((paletteSize & 1) != 0)
      throw new InvalidDataException("CDXL palette size must be even (each entry = 2 bytes).");

    var rowBytes = (width + 7) >> 3;
    var planeSize = rowBytes * height;
    var bitmapSize = planes * planeSize;
    var expectedFrame = paletteSize + bitmapSize + audioSize;
    if (data.Length < _HEADER_SIZE + expectedFrame)
      throw new InvalidDataException($"CDXL data {data.Length} bytes < header {_HEADER_SIZE} + frame {expectedFrame}.");

    var palette = data.Slice(_HEADER_SIZE, paletteSize).ToArray();
    var pixels = data.Slice(_HEADER_SIZE + paletteSize, bitmapSize).ToArray();

    return new() {
      Width = width,
      Height = height,
      BitPlanes = planes,
      Palette = palette,
      PixelData = pixels,
    };
  }
}
