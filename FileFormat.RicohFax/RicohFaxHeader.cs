using FileFormat.Core;

namespace FileFormat.RicohFax;

[GenerateSerializer]
internal readonly partial record struct RicohFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Resolution,
  ushort Compression
) {
  public const int StructSize = 12;
}
