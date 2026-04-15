using FileFormat.Core;

namespace FileFormat.AccessFax;

[GenerateSerializer]
internal readonly partial record struct AccessFaxHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort Flags
) {
  public const int StructSize = 8;
}
