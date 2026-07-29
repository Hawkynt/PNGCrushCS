using FileFormat.Core;

namespace FileFormat.Pdn;

/// <summary>The 16-byte header at the start of every PDN file: Magic "PDN3" (4 bytes) + Version (LE uint16) + Reserved (LE uint16) + Width (LE uint32) + Height (LE uint32).</summary>
[GenerateSerializer]
internal readonly partial record struct PdnHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  ushort Version,
  ushort Reserved,
  uint Width,
  uint Height
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PdnHeader>();
}
