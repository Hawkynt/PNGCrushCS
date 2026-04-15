using FileFormat.Core;

namespace FileFormat.Olpc565;

/// <summary>The 4-byte header at the start of every OLPC 565 file: Width (ushort LE), Height (ushort LE).</summary>
[GenerateSerializer]
public readonly partial record struct Olpc565Header( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Olpc565Header>();
}
