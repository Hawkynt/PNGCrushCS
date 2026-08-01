using System;

namespace FileFormat.Viff;

/// <summary>Assembles VIFF (Khoros Visualization Image File Format) file bytes from pixel data.</summary>
public static class ViffWriter {

  public static byte[] ToBytes(ViffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(ViffFile file) {
    var pixelBytes = file.StorageType == ViffStorageType.Bit
      ? ((file.Width + 7) / 8) * file.Height * file.Bands
      : file.Width * file.Height * file.Bands * _GetBytesPerElement(file.StorageType);

    var hasMap = file.MapData is { Length: > 0 };
    var mapBytes = hasMap ? file.MapData!.Length : 0;
    var result = new byte[ViffHeader.StructSize + mapBytes + pixelBytes];
    var span = result.AsSpan();

    var header = new ViffHeader(
      Identifier: ViffHeader.Magic,
      FileType: 1,
      Release: 1,
      Version: 3,
      MachineDep: 0x02,
      Comment: file.Comment,
      RowSize: (uint)file.Width,
      ColSize: (uint)file.Height,
      SubRowSize: 0,
      // -1 is the "no offset given" sentinel Khoros writers use, and what ImageMagick emits.
      StartX: -1,
      StartY: -1,
      PixelSizeX: 1f,
      PixelSizeY: 1f,
      LocationType: 1, // VFF_LOC_IMPLICIT — pixels on a regular grid. Anything else is rejected elsewhere.
      LocationDim: 0,
      NumberOfImages: 1,
      NumberDataBands: (uint)file.Bands,
      DataStorageType: (uint)file.StorageType,
      DataEncodeScheme: 0, // VFF_DES_RAW
      MapScheme: (uint)(hasMap ? file.MapScheme : ViffMapScheme.None),
      MapStorageType: (uint)(hasMap ? file.MapType : ViffMapType.None),
      MapRowSize: hasMap ? (uint)file.MapRowSize : 0,
      MapColSize: hasMap ? (uint)file.MapColSize : 0,
      MapSubRowSize: 0,
      MapEnable: 1, // VFF_MAP_OPTIONAL
      MapsPerCycle: 0,
      ColorSpaceModel: (uint)file.ColorSpaceModel,
      ISpare1: 0,
      ISpare2: 0,
      FSpare1: 0f,
      FSpare2: 0f
    );

    header.WriteTo(span);

    var offset = ViffHeader.StructSize;

    if (hasMap) {
      file.MapData!.CopyTo(span[offset..]);
      offset += mapBytes;
    }

    var copyLen = Math.Min(pixelBytes, file.PixelData.Length);
    if (copyLen > 0)
      file.PixelData.AsSpan(0, copyLen).CopyTo(span[offset..]);

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
    ViffStorageType.DoubleComplex => 16,
    _ => 1
  };
}
