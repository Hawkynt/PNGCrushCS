using FileFormat.Core;

namespace FileFormat.Qrt;

/// <summary>The 10-byte header of a QRT ray tracer image: Width(ushort LE), Height(ushort LE), Reserved(6 bytes).</summary>
[GenerateSerializer]
[Filler(4, 6)]
public readonly partial record struct QrtHeader( ushort Width, ushort Height
) {

 public const int StructSize = 10;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<QrtHeader>();
}
