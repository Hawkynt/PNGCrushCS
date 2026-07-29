using FileFormat.Core;

namespace FileFormat.QuantelVpb;

[GenerateSerializer]
internal readonly partial record struct QuantelVpbHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bpp,
  ushort Fields,
  uint Reserved
) {
  public const int StructSize = 16;
}
