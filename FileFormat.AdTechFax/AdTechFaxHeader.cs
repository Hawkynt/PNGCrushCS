using FileFormat.Core;

namespace FileFormat.AdTechFax;

[GenerateSerializer]
internal readonly partial record struct AdTechFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Resolution,
  ushort Reserved
) {
  public const int StructSize = 12;
}
