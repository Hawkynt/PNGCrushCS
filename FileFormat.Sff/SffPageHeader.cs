using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>The 18-byte header at the start of each SFF page. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct SffPageHeader(
  ushort PageDataLength,
  byte ResolutionVertical,
  byte ResolutionHorizontal,
  byte Coding,
  byte Reserved,
  ushort LineLength,
  ushort PageHeight,
  uint PreviousPageOffset,
  uint NextPageOffset
) {

 public const int StructSize = 18;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SffPageHeader>();
}
