using FileFormat.Core;

namespace FileFormat.Nie;

[GenerateSerializer]
internal readonly partial record struct NieHeader(
  [property: FieldOffset(8)] uint Width,
  uint Height
) {
  public const int StructSize = 16;
}
