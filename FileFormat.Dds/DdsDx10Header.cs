using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>The optional 20-byte DDS_HEADER_DXT10 structure present when FourCC is "DX10".</summary>
[GenerateSerializer]
public readonly partial record struct DdsDx10Header(
  int DxgiFormat,
  int ResourceDimension,
  int MiscFlag,
  int ArraySize,
  int MiscFlags2
) {

 public const int StructSize = 20;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<DdsDx10Header>();
}
