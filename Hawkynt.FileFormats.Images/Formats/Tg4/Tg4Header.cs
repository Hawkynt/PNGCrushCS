using FileFormat.Core;

namespace FileFormat.Tg4;

[GenerateSerializer]
internal readonly partial record struct Tg4Header(
  [property: FieldOffset(4)] ushort Width,
  ushort Height
) {
  public const int StructSize = 8;
}
