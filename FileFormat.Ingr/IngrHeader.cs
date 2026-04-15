using FileFormat.Core;

namespace FileFormat.Ingr;

/// <summary>Sparse fields from the 512-byte INGR header (little-endian).</summary>
[GenerateSerializer]
[Filler(4, 4)]
[Filler(12, 172)]
[Filler(192, 320)]
internal readonly partial record struct IngrHeader(
  [property: Field(0, 2)] ushort HeaderType,
  [property: Field(2, 2)] ushort DataTypeCode,
  [property: Field(8, 2)] short XExtent,
  [property: Field(10, 2)] short YExtent,
  [property: Field(184, 4)] int PixelsPerLine,
  [property: Field(188, 4)] int NumberOfLines
) {
  public const int StructSize = 512;
}
