using System;
using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 22-byte placeable WMF header preceding the standard WMF header.</summary>
[GenerateSerializer]
public readonly partial record struct WmfPlaceableHeader(
  [property: HeaderField(0, 4)] uint Magic,
  [property: HeaderField(4, 2)] ushort Handle,
  [property: HeaderField(6, 2)] short Left,
  [property: HeaderField(8, 2)] short Top,
  [property: HeaderField(10, 2)] short Right,
  [property: HeaderField(12, 2)] short Bottom,
  [property: HeaderField(14, 2)] ushort Inch,
  [property: HeaderField(16, 4)] uint Reserved,
  [property: HeaderField(20, 2)] ushort Checksum
) {

  public const int StructSize = 22;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WmfPlaceableHeader>();

  public static ushort ComputeChecksum(ReadOnlySpan<byte> headerBytes) {
    ushort checksum = 0;
    for (var i = 0; i < 10; ++i)
      checksum ^= System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[(i * 2)..]);
    return checksum;
  }
}
