using FileFormat.Core;

namespace FileFormat.Wad2;

/// <summary>The 12-byte header at the start of every WAD2 file.</summary>
[GenerateSerializer]
[Filler(0, 4)]
public readonly partial record struct Wad2Header( byte Magic1, byte Magic2, byte Magic3, byte Magic4, int NumLumps, int DirectoryOffset
) {

 public const int StructSize = 12;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Wad2Header>();
}
