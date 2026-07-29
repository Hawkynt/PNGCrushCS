using FileFormat.Core;

namespace FileFormat.NistIHead;

[GenerateSerializer]
internal readonly partial record struct NistIHeadHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Compression,
  uint Reserved
) {
  public const int StructSize = 16;
}
