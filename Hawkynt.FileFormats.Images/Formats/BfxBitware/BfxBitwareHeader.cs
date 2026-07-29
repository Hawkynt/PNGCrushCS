using FileFormat.Core;

namespace FileFormat.BfxBitware;

[GenerateSerializer]
internal readonly partial record struct BfxBitwareHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Compression
) {
  public const int StructSize = 12;
}
