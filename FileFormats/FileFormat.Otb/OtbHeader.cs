using FileFormat.Core;

namespace FileFormat.Otb;

/// <summary>The 4-byte header at the start of every OTB file: InfoField(0x00), Width, Height, Depth(0x01).</summary>
[GenerateSerializer]
public readonly partial record struct OtbHeader( byte InfoField, byte Width, byte Height, byte Depth
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<OtbHeader>();
}
