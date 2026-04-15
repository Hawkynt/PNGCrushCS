using FileFormat.Core;

namespace FileFormat.CokeAtari;

/// <summary>The 4-byte header of a COKE file: Width (ushort BE), Height (ushort BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct CokeAtariHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<CokeAtariHeader>();
}
