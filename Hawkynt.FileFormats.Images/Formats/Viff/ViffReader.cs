using System;
using System.IO;

namespace FileFormat.Viff;

/// <summary>Reads VIFF (Khoros Visualization Image File Format) files from bytes, streams, or file paths.</summary>
public static class ViffReader {

  public static ViffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VIFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ViffFile FromStream(Stream stream) {
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

  public static ViffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data.AsSpan());
  }

  public static ViffFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < ViffHeader.StructSize)
      throw new InvalidDataException($"Data too small for a valid VIFF file: expected at least {ViffHeader.StructSize} bytes, got {data.Length}.");

    if (data[0] != ViffHeader.Magic)
      throw new InvalidDataException($"Invalid VIFF magic byte: expected 0x{ViffHeader.Magic:X2}, got 0x{data[0]:X2}.");

    var header = ViffHeader.ReadFrom(data);
    var width = (int)header.RowSize;
    var height = (int)header.ColSize;
    var bands = (int)header.NumberDataBands;

    if (width <= 0)
      throw new InvalidDataException($"Invalid VIFF width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid VIFF height: {height}.");
    if (bands <= 0)
      bands = 1;

    var storageType = (ViffStorageType)header.DataStorageType;
    var mapScheme = (ViffMapScheme)header.MapScheme;
    var mapType = (ViffMapType)header.MapStorageType;
    var offset = ViffHeader.StructSize;

    // The map sits between the header and the pixels, so its size has to come out right even when
    // nothing goes on to read it — miss by a byte and the image starts in the wrong place. VFF_MS_NONE
    // is what answers "is this paletted": map_enable stays at VFF_MAP_OPTIONAL on unmapped files too.
    byte[]? mapData = null;
    if (mapScheme != ViffMapScheme.None && header.MapRowSize > 0 && header.MapColSize > 0) {
      var mapBytes = (int)(header.MapRowSize * header.MapColSize * _GetMapBytesPerElement(mapType));
      if (mapBytes > 0 && offset + mapBytes <= data.Length) {
        mapData = new byte[mapBytes];
        data.Slice(offset, mapBytes).CopyTo(mapData);
        offset += mapBytes;
      }
    }

    // Bit storage packs each row into ceil(width/8) bytes; every other type is one element a pixel.
    var pixelBytes = storageType == ViffStorageType.Bit
      ? ((width + 7) / 8) * height * bands
      : width * height * bands * _GetBytesPerElement(storageType);

    var pixelData = new byte[pixelBytes];
    var copyLen = Math.Min(pixelBytes, data.Length - offset);
    if (copyLen > 0)
      data.Slice(offset, copyLen).CopyTo(pixelData);

    return new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = storageType,
      ColorSpaceModel = (ViffColorSpaceModel)header.ColorSpaceModel,
      Comment = header.Comment,
      PixelData = pixelData,
      MapData = mapData,
      MapScheme = mapScheme,
      MapType = mapType,
      MapRowSize = (int)header.MapRowSize,
      MapColSize = (int)header.MapColSize
    };
  }

  private static int _GetBytesPerElement(ViffStorageType storageType) => storageType switch {
    ViffStorageType.Bit => 1,
    ViffStorageType.Byte => 1,
    ViffStorageType.Short => 2,
    ViffStorageType.Int => 4,
    ViffStorageType.Float => 4,
    ViffStorageType.Double => 8,
    ViffStorageType.Complex => 8,
    ViffStorageType.DoubleComplex => 16,
    _ => 1
  };

  private static int _GetMapBytesPerElement(ViffMapType mapType) => mapType switch {
    ViffMapType.None => 0,
    ViffMapType.Byte => 1,
    ViffMapType.Short => 2,
    ViffMapType.Int => 4,
    ViffMapType.Float => 4,
    ViffMapType.Double => 8,
    ViffMapType.Complex => 8,
    _ => 1
  };
}
