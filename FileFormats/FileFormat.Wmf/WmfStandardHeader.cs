using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 18-byte standard WMF header following the placeable header.</summary>
[GenerateSerializer]
public readonly partial record struct WmfStandardHeader(
  ushort Type,
  ushort HeaderSize,
  ushort Version,
  uint FileSizeInWords,
  ushort NumObjects,
  uint MaxRecordSize,
  ushort NumMembers
) {

 public const int StructSize = 18;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WmfStandardHeader>();
}
