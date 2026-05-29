using FileFormat.Core;

namespace FileFormat.HomeworldLif;

[GenerateSerializer]
internal readonly partial record struct HomeworldLifHeader(
  [property: FieldOffset(4)] int Version,
  int Width,
  int Height
) {
  public const int StructSize = 16;
}
