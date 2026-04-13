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

  public static ViffFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < ViffHeader.StructSize)
      throw new InvalidDataException($"Data too small for a valid VIFF file: expected at least {ViffHeader.StructSize} bytes, got {data.Length}.");

    if (data[0] != ViffHeader.Magic)
      throw new InvalidDataException($"Invalid VIFF magic byte: expected 0x{ViffHeader.Magic:X2}, got 0x{data[0]:X2}.");

    var header = ViffHeader.ReadFrom(data);
    var width = (int)header.RowSize;
    var height = (int)header.ColSize;
    var bands = (int)header.SubRowSize;

    if (width <= 0)
      throw new InvalidDataException($"Invalid VIFF width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid VIFF height: {height}.");
    if (bands <= 0)
      bands = 1;

    var bytesPerElement = _GetBytesPerElement((ViffStorageType)header.DataStorageType);
    var offset = ViffHeader.StructSize;

    // Read map data if enabled
    byte[]? mapData = null;
    if (header.MapEnable != 0 && header.MapRowSize > 0 && header.MapColSize > 0) {
      var mapBytesPerElement = _GetMapBytesPerElement((ViffMapType)header.MapType);
      var mapBytes = (int)(header.MapRowSize * header.MapColSize * mapBytesPerElement);
      if (offset + mapBytes <= data.Length) {
        mapData = new byte[mapBytes];
        data.Slice(offset, mapBytes).CopyTo(mapData.AsSpan(0));
        offset += mapBytes;
      }
    }

    // Read pixel data
    var pixelBytes = width * height * bands * bytesPerElement;
    var available = data.Length - offset;
    var copyLen = Math.Min(pixelBytes, available);
    var pixelData = new byte[pixelBytes];
    if (copyLen > 0)
      data.Slice(offset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = (ViffStorageType)header.DataStorageType,
      ColorSpaceModel = (ViffColorSpaceModel)header.ColorSpaceModel,
      Comment = header.Comment,
      PixelData = pixelData,
      MapData = mapData,
      MapType = (ViffMapType)header.MapType,
      MapRowSize = (int)header.MapRowSize,
      MapColSize = (int)header.MapColSize,
      MapStorageType = (ViffStorageType)header.MapStorageType
    };
    }

  public static ViffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ViffHeader.StructSize)
      throw new InvalidDataException($"Data too small for a valid VIFF file: expected at least {ViffHeader.StructSize} bytes, got {data.Length}.");

    if (data[0] != ViffHeader.Magic)
      throw new InvalidDataException($"Invalid VIFF magic byte: expected 0x{ViffHeader.Magic:X2}, got 0x{data[0]:X2}.");

    var header = ViffHeader.ReadFrom(data.AsSpan());
    var width = (int)header.RowSize;
    var height = (int)header.ColSize;
    var bands = (int)header.SubRowSize;

    if (width <= 0)
      throw new InvalidDataException($"Invalid VIFF width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid VIFF height: {height}.");
    if (bands <= 0)
      bands = 1;

    var bytesPerElement = _GetBytesPerElement((ViffStorageType)header.DataStorageType);
    var offset = ViffHeader.StructSize;

    // Read map data if enabled
    byte[]? mapData = null;
    if (header.MapEnable != 0 && header.MapRowSize > 0 && header.MapColSize > 0) {
      var mapBytesPerElement = _GetMapBytesPerElement((ViffMapType)header.MapType);
      var mapBytes = (int)(header.MapRowSize * header.MapColSize * mapBytesPerElement);
      if (offset + mapBytes <= data.Length) {
        mapData = new byte[mapBytes];
        data.AsSpan(offset, mapBytes).CopyTo(mapData.AsSpan(0));
        offset += mapBytes;
      }
    }

    // Read pixel data
    var pixelBytes = width * height * bands * bytesPerElement;
    var available = data.Length - offset;
    var copyLen = Math.Min(pixelBytes, available);
    var pixelData = new byte[pixelBytes];
    if (copyLen > 0)
      data.AsSpan(offset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = (ViffStorageType)header.DataStorageType,
      ColorSpaceModel = (ViffColorSpaceModel)header.ColorSpaceModel,
      Comment = header.Comment,
      PixelData = pixelData,
      MapData = mapData,
      MapType = (ViffMapType)header.MapType,
      MapRowSize = (int)header.MapRowSize,
      MapColSize = (int)header.MapColSize,
      MapStorageType = (ViffStorageType)header.MapStorageType
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
    _ => 1
  };

  private static int _GetMapBytesPerElement(ViffMapType mapType) => mapType switch {
    ViffMapType.None => 0,
    ViffMapType.Byte => 1,
    ViffMapType.Short => 2,
    ViffMapType.Int => 4,
    ViffMapType.Float => 4,
    ViffMapType.Double => 8,
    _ => 1
  };
}
