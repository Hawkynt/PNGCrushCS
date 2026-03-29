using FileFormat.Core;

namespace FileFormat.Art;

/// <summary>The 16-byte header at the start of every Build Engine ART file.</summary>
[GenerateSerializer]
public readonly partial record struct ArtHeader(
  [property: HeaderField(0, 4)] int Version,
  [property: HeaderField(4, 4)] int NumTiles,
  [property: HeaderField(8, 4)] int TileStart,
  [property: HeaderField(12, 4)] int TileEnd
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ArtHeader>();
}
