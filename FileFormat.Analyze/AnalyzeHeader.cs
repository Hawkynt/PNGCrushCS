using FileFormat.Core;

namespace FileFormat.Analyze;

/// <summary>Sparse fields from the 348-byte Analyze 7.5 header (little-endian).</summary>
[GenerateSerializer]
[Filler(4, 36)]
[Filler(46, 24)]
[Filler(74, 274)]
internal readonly partial record struct AnalyzeHeader(
  [property: Field(0, 4)] int SizeofHdr,
  [property: Field(42, 2)] short Width,
  [property: Field(44, 2)] short Height,
  [property: Field(70, 2)] short DataType,
  [property: Field(72, 2)] short BitPix
) {
  public const int StructSize = 348;
}
