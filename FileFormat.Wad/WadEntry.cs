using FileFormat.Core;

namespace FileFormat.Wad;

/// <summary>A 16-byte directory entry within a WAD file.</summary>
[GenerateSerializer]
public readonly partial record struct WadEntry( int FilePos, int Size, string Name
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WadEntry>();
}
