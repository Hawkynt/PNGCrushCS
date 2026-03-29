using FileFormat.Core;

namespace FileFormat.Acorn;

/// <summary>The 44-byte header at the start of every sprite within an Acorn sprite file.</summary>
[GenerateSerializer]
public readonly partial record struct AcornSpriteHeader(
  [property: HeaderField(0, 4)] int NextSpriteOffset,
  [property: HeaderField(4, 12)] string Name,
  [property: HeaderField(16, 4)] int WidthInWords,
  [property: HeaderField(20, 4)] int HeightInScanlines,
  [property: HeaderField(24, 4)] int FirstBitUsed,
  [property: HeaderField(28, 4)] int LastBitUsed,
  [property: HeaderField(32, 4)] int ImageOffset,
  [property: HeaderField(36, 4)] int MaskOffset,
  [property: HeaderField(40, 4)] int Mode
) {

  public const int StructSize = 44;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AcornSpriteHeader>();
}
