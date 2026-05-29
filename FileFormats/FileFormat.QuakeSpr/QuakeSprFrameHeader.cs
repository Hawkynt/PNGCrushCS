using FileFormat.Core;

namespace FileFormat.QuakeSpr;

/// <summary>The 20-byte per-frame header in a Quake 1 sprite (.spr) file.</summary>
[GenerateSerializer]
public readonly partial record struct QuakeSprFrameHeader(
  int FrameType,
  int OriginX,
  int OriginY,
  int Width,
  int Height
) {

 public const int StructSize = 20;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<QuakeSprFrameHeader>();
}
