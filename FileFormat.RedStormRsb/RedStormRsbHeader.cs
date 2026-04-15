using FileFormat.Core;

namespace FileFormat.RedStormRsb;

[GenerateSerializer]
internal readonly partial record struct RedStormRsbHeader(
  [property: FieldOffset(4)] ushort Version,
  ushort Width,
  ushort Height,
  ushort Bpp
) {
  public const int StructSize = 12;
}
