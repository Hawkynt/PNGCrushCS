using FileFormat.Core;

namespace FileFormat.Wad;

/// <summary>The 12-byte header at the start of every WAD file.</summary>
[GenerateSerializer]
[Filler(0, 4)]
public readonly partial record struct WadHeader( byte Id1, byte Id2, byte Id3, byte Id4, int NumLumps, int DirectoryOffset
) {

 public const int StructSize = 12;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WadHeader>();
}
