using FileFormat.Core;

namespace FileFormat.SmartFax;

[GenerateSerializer]
internal readonly partial record struct SmartFaxHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Flags
) {
  public const int StructSize = 10;
}
