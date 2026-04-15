using FileFormat.Core;

namespace FileFormat.CanonNavFax;

[GenerateSerializer]
internal readonly partial record struct CanonNavFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Resolution,
  ushort Encoding
) {
  public const int StructSize = 12;
}
