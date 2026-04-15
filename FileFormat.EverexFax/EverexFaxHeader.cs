using FileFormat.Core;

namespace FileFormat.EverexFax;

[GenerateSerializer]
internal readonly partial record struct EverexFaxHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Pages,
  ushort Compression,
  ushort Reserved
) {
  public const int StructSize = 16;
}
