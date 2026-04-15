using FileFormat.Core;

namespace FileFormat.DrawIt;

/// <summary>The 4-byte header at the start of every DrawIt file: Width (LE 16-bit) + Height (LE 16-bit).</summary>
[GenerateSerializer]
internal readonly partial record struct DrawItHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<DrawItHeader>();
}
