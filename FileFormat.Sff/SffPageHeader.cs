using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>The 18-byte header at the start of each SFF page. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct SffPageHeader(
  [property: HeaderField(0, 2)] ushort PageDataLength,
  [property: HeaderField(2, 1)] byte ResolutionVertical,
  [property: HeaderField(3, 1)] byte ResolutionHorizontal,
  [property: HeaderField(4, 1)] byte Coding,
  [property: HeaderField(5, 1)] byte Reserved,
  [property: HeaderField(6, 2)] ushort LineLength,
  [property: HeaderField(8, 2)] ushort PageHeight,
  [property: HeaderField(10, 4)] uint PreviousPageOffset,
  [property: HeaderField(14, 4)] uint NextPageOffset
) {

  public const int StructSize = 18;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SffPageHeader>();
}
