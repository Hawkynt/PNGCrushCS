using FileFormat.Core;

namespace FileFormat.AvhrrImage;

[GenerateSerializer]
internal readonly partial record struct AvhrrImageHeader(
  [property: FieldOffset(4)] ushort Width,
  ushort Height,
  ushort Bands,
  ushort DataType
) {
  public const int StructSize = 12;
}
