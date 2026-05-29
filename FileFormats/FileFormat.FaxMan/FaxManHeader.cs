using FileFormat.Core;

namespace FileFormat.FaxMan;

[GenerateSerializer]
internal readonly partial record struct FaxManHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort Version,
  ushort Flags
) {
  public const int StructSize = 10;
}
