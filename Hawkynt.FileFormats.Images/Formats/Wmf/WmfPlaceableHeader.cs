using System;
using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 22-byte placeable WMF header preceding the standard WMF header.</summary>
[GenerateSerializer]
public readonly partial record struct WmfPlaceableHeader(
  uint Magic,
  ushort Handle,
  short Left,
  short Top,
  short Right,
  short Bottom,
  ushort Inch,
  uint Reserved,
  ushort Checksum
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
