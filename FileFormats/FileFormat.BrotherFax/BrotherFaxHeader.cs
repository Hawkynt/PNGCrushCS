using FileFormat.Core;

namespace FileFormat.BrotherFax;

[GenerateSerializer]
internal readonly partial record struct BrotherFaxHeader(
  [property: FieldOffset(2)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Compression
) {
  public const int StructSize = 10;
}
