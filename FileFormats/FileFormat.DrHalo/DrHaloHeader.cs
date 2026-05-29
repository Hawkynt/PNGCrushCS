using FileFormat.Core;

namespace FileFormat.DrHalo;

/// <summary>The 6-byte header at the start of every Dr. Halo CUT file.</summary>
[GenerateSerializer]
public readonly partial record struct DrHaloHeader( short Width, short Height, short Reserved
) {

 public const int StructSize = 6;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<DrHaloHeader>();
}
