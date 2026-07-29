using System;

namespace FileFormat.Viff;

/// <summary>Assembles VIFF (Khoros Visualization Image File Format) file bytes from pixel data.</summary>
public static class ViffWriter {

  public static byte[] ToBytes(ViffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(ViffFile file) {
    var bytesPerElement = _GetBytesPerElement(file.StorageType);
    var pixelBytes = file.Width * file.Height * file.Bands * bytesPerElement;
    var mapBytes = file.MapData?.Length ?? 0;
    var totalSize = ViffHeader.StructSize + mapBytes + pixelBytes;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    var hasMap = file.MapData is { Length: > 0 };
    var mapBytesPerElement = _GetMapBytesPerElement(file.MapType);

    var header = new ViffHeader(
      Identifier: ViffHeader.Magic,
      FileType: 1,
      Release: 1,
      Version: 3,
      MachineDep: 0x02,
      Comment: file.Comment,
      RowSize: (uint)file.Width,
      ColSize: (uint)file.Height,
      SubRowSize: (uint)file.Bands,
      StartX: 0f,
      StartY: 0f,
      PixelSize: 0f,
      Location: 0,
      Padding: 0,
      FileSpare: 0,
      MapType: (uint)file.MapType,
      MapRowSize: hasMap ? (uint)file.MapRowSize : 0,
      MapColSize: hasMap ? (uint)file.MapColSize : 0,
      MapSubRowSize: 0,
      MapStorageType: (uint)file.MapStorageType,
      MapRowSizePad: 0,
      MapEnable: hasMap ? 1u : 0u,
      MapsPerCycle: 0,
      ColorSpaceModel: (uint)file.ColorSpaceModel,
      IsBand: file.Bands > 1 ? 1u : 0u,
      DataStorageType: (uint)file.StorageType,
      DataEncode: 0,
      MapScheme0: 0f,
      MapScheme1: 0f
    );

    header.WriteTo(span);

    var offset = ViffHeader.StructSize;

    if (hasMap) {
      file.MapData!.AsSpan(0, mapBytes).CopyTo(result.AsSpan(offset));
      offset += mapBytes;
    }

    var copyLen = Math.Min(pixelBytes, file.PixelData.Length);
    if (copyLen > 0)
      file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(offset));

    return result;
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
