using FileFormat.Core;

namespace FileFormat.QuakeSpr;

/// <summary>The 36-byte header at the start of every Quake 1 sprite (.spr) file.</summary>
[GenerateSerializer]
public readonly partial record struct QuakeSprHeader(
  uint Magic,
  int Version,
  int SpriteType,
  float BoundingRadius,
  int MaxWidth,
  int MaxHeight,
  int NumFrames,
  float BeamLength,
  int SyncType
) {

 public const int StructSize = 36;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<QuakeSprHeader>();
}
