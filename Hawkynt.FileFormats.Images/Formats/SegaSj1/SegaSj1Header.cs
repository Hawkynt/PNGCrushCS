using FileFormat.Core;

namespace FileFormat.SegaSj1;

[GenerateSerializer]
internal readonly partial record struct SegaSj1Header(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Flags
) {
  public const int StructSize = 12;
}
