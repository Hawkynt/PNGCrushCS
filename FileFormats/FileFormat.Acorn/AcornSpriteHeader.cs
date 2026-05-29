using FileFormat.Core;

namespace FileFormat.Acorn;

/// <summary>The 44-byte header at the start of every sprite within an Acorn sprite file.</summary>
[GenerateSerializer]
public readonly partial record struct AcornSpriteHeader(
  int NextSpriteOffset,
  [property: String, SeqField(Size = 12)] string Name,
  int WidthInWords,
  int HeightInScanlines,
  int FirstBitUsed,
  int LastBitUsed,
  int ImageOffset,
  int MaskOffset,
  int Mode
) {

 public const int StructSize = 44;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AcornSpriteHeader>();
}
