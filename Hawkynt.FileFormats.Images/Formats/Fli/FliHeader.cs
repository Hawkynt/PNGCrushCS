using FileFormat.Core;

namespace FileFormat.Fli;

/// <summary>The 128-byte header at the start of every FLI/FLC file.</summary>
[GenerateSerializer]
[Filler(20, 108)]
public readonly partial record struct FliHeader( int Size, short Magic, short Frames, short Width, short Height, short Depth, short Flags, int Speed
) {

 public const int StructSize = 128;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FliHeader>();
}
