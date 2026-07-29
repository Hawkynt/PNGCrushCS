using FileFormat.Core;

namespace FileFormat.OlicomFax;

[GenerateSerializer]
internal readonly partial record struct OlicomFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Flags
) {
  public const int StructSize = 10;
}
