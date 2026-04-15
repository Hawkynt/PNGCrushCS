using FileFormat.Core;

namespace FileFormat.WorldportFax;

[GenerateSerializer]
internal readonly partial record struct WorldportFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Flags
) {
  public const int StructSize = 10;
}
