using System;
using System.IO;

namespace FileFormat.AmigaIcon;

/// <summary>Reads Amiga Workbench icon (.info) files from bytes, streams, or file paths.</summary>
public static class AmigaIconReader {

  public static AmigaIconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Amiga icon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AmigaIconFile FromStream(Stream stream) {
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

  public static AmigaIconFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AmigaIconFile.PlanarDataOffset)
      throw new InvalidDataException($"Data too small for a valid Amiga icon file: expected at least {AmigaIconFile.PlanarDataOffset} bytes, got {data.Length}.");

    var header = AmigaIconHeader.ReadFrom(data);

    if (header.Magic != AmigaIconHeader.MagicValue)
      throw new InvalidDataException($"Invalid Amiga icon magic: expected 0x{AmigaIconHeader.MagicValue:X4}, got 0x{header.Magic:X4}.");

    var width = _ReadImageInt16(data, 4);
    var height = _ReadImageInt16(data, 6);
    var depth = _ReadImageInt16(data, 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid icon width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid icon height: {height}.");
    if (depth is < 1 or > 8)
      throw new InvalidDataException($"Invalid icon depth: {depth}.");

    var hasImage = _ReadImageUInt32(data, 10) != 0;
    if (!hasImage)
      throw new InvalidDataException("Icon has no image data (ImageDataPointer is zero).");

    var expectedPlanarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var offset = AmigaIconFile.PlanarDataOffset;

    if (data.Length < offset + expectedPlanarSize)
      throw new InvalidDataException($"Data too small for planar image data: expected {offset + expectedPlanarSize} bytes, got {data.Length}.");

    var planarData = new byte[expectedPlanarSize];
    data.Slice(offset, expectedPlanarSize).CopyTo(planarData.AsSpan(0));

    var rawHeader = new byte[AmigaIconHeader.StructSize];
    data.Slice(0, AmigaIconHeader.StructSize).CopyTo(rawHeader.AsSpan(0));

    return new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      IconType = header.IconTypeByte,
      PlanarData = planarData,
      RawHeader = rawHeader,
    };
    }

  public static AmigaIconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads a big-endian 16-bit field from the Image structure that follows the DiskObject.</summary>
  private static int _ReadImageInt16(ReadOnlySpan<byte> data, int fieldOffset) {
    var offset = AmigaIconFile.ImageStructOffset + fieldOffset;
    return offset + 1 < data.Length ? (data[offset] << 8) | data[offset + 1] : 0;
  }

  private static int _ReadImageInt16(byte[] data, int fieldOffset) => _ReadImageInt16(data.AsSpan(), fieldOffset);

  /// <summary>Reads the big-endian 32-bit ImageData pointer from the Image structure.</summary>
  private static uint _ReadImageUInt32(ReadOnlySpan<byte> data, int fieldOffset) {
    var offset = AmigaIconFile.ImageStructOffset + fieldOffset;
    if (offset + 3 >= data.Length)
      return 0;

    return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
         | ((uint)data[offset + 2] << 8) | data[offset + 3];
  }

  private static uint _ReadImageUInt32(byte[] data, int fieldOffset) => _ReadImageUInt32(data.AsSpan(), fieldOffset);
}
