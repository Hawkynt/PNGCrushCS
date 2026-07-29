using FileFormat.Core;

namespace FileFormat.HfImage;

[GenerateSerializer]
internal readonly partial record struct HfImageHeader(
  [property: FieldOffset(2)] ushort Width,
  ushort Height,
  ushort DataType
) {
  public const int StructSize = 8;
}
