using FileFormat.Core;

namespace FileFormat.Art;

/// <summary>The 16-byte header at the start of every Build Engine ART file.</summary>
[GenerateSerializer]
public readonly partial record struct ArtHeader( int Version, int NumTiles, int TileStart, int TileEnd
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<ArtHeader>();
}
