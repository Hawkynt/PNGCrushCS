using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 12-byte table-of-contents entry (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorTocEntry( uint Type, uint Subtype, uint Position
) {

 public const int StructSize = 12;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<XcursorTocEntry>();
}
