using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 18-byte standard WMF header following the placeable header.</summary>
[GenerateSerializer]
public readonly partial record struct WmfStandardHeader(
  [property: HeaderField(0, 2)] ushort Type,
  [property: HeaderField(2, 2)] ushort HeaderSize,
  [property: HeaderField(4, 2)] ushort Version,
  [property: HeaderField(6, 4)] uint FileSizeInWords,
  [property: HeaderField(10, 2)] ushort NumObjects,
  [property: HeaderField(12, 4)] uint MaxRecordSize,
  [property: HeaderField(16, 2)] ushort NumMembers
) {

  public const int StructSize = 18;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WmfStandardHeader>();
}
