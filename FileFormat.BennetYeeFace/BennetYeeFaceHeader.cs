using FileFormat.Core;

namespace FileFormat.BennetYeeFace;

/// <summary>The 4-byte header at the start of every YBM file: Width (ushort LE), Height (ushort LE).</summary>
[GenerateSerializer]
public readonly partial record struct BennetYeeFaceHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BennetYeeFaceHeader>();
}
