using FileFormat.Core;

namespace FileFormat.QuakeSpr;

/// <summary>The 36-byte header at the start of every Quake 1 sprite (.spr) file.</summary>
[GenerateSerializer]
public readonly partial record struct QuakeSprHeader(
  [property: HeaderField(0, 4)] uint Magic,
  [property: HeaderField(4, 4)] int Version,
  [property: HeaderField(8, 4)] int SpriteType,
  [property: HeaderField(12, 4)] float BoundingRadius,
  [property: HeaderField(16, 4)] int MaxWidth,
  [property: HeaderField(20, 4)] int MaxHeight,
  [property: HeaderField(24, 4)] int NumFrames,
  [property: HeaderField(28, 4)] float BeamLength,
  [property: HeaderField(32, 4)] int SyncType
) {

  public const int StructSize = 36;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<QuakeSprHeader>();
}
