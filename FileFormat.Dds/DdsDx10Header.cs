using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The optional 20-byte DDS_HEADER_DXT10 structure present when FourCC is "DX10".</summary>
[GenerateSerializer]
public readonly partial record struct DdsDx10Header(
  [property: HeaderField(0, 4)] int DxgiFormat,
  [property: HeaderField(4, 4)] int ResourceDimension,
  [property: HeaderField(8, 4)] int MiscFlag,
  [property: HeaderField(12, 4)] int ArraySize,
  [property: HeaderField(16, 4)] int MiscFlags2
) {

  public const int StructSize = 20;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DdsDx10Header>();
}
