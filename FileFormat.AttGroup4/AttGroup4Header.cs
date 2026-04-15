using FileFormat.Core;

namespace FileFormat.AttGroup4;

[GenerateSerializer]
internal readonly partial record struct AttGroup4Header(
  [property: FieldOffset(4)] ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
