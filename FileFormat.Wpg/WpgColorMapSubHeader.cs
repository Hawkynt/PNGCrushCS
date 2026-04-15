using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 4-byte sub-header inside a ColorMap record: startIndex, numEntries (both LE uint16).</summary>
[GenerateSerializer]
internal readonly partial record struct WpgColorMapSubHeader(
  ushort StartIndex,
  ushort NumEntries
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WpgColorMapSubHeader>();
}
