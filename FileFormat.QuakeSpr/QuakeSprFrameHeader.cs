using FileFormat.Core;

namespace FileFormat.QuakeSpr;

/// <summary>The 20-byte per-frame header in a Quake 1 sprite (.spr) file.</summary>
[GenerateSerializer]
public readonly partial record struct QuakeSprFrameHeader(
  [property: HeaderField(0, 4)] int FrameType,
  [property: HeaderField(4, 4)] int OriginX,
  [property: HeaderField(8, 4)] int OriginY,
  [property: HeaderField(12, 4)] int Width,
  [property: HeaderField(16, 4)] int Height
) {

  public const int StructSize = 20;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<QuakeSprFrameHeader>();
}
