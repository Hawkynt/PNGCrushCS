using FileFormat.Core;

namespace FileFormat.Acorn;

/// <summary>The 12-byte sprite area header at the start of an Acorn sprite file: spriteCount(4) + firstSpriteOffset(4) + freeWordOffset(4).</summary>
[GenerateSerializer]
public readonly partial record struct AcornAreaHeader(
  int SpriteCount,
  int FirstSpriteOffset,
  int FreeWordOffset
) {

 public const int StructSize = 12;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AcornAreaHeader>();
}
