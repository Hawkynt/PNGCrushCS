using FileFormat.Core;

namespace FileFormat.Pcd;

/// <summary>The 4-byte dimension header of a PCD file at offset 2056: Width (LE uint16), Height (LE uint16).</summary>
[GenerateSerializer]
internal readonly partial record struct PcdHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PcdHeader>();
}
