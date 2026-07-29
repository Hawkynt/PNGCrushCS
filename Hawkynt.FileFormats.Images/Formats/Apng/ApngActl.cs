using FileFormat.Core;

namespace FileFormat.Apng;

/// <summary>The 8-byte acTL (Animation Control) chunk data in an APNG file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct ApngActl( int NumFrames, int NumPlays
) {

 public const int StructSize = 8;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<ApngActl>();
}
