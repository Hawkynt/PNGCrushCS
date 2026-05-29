using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 16-byte file header (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorFileHeader( uint Magic, uint HeaderSize, uint Version, uint TocCount
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<XcursorFileHeader>();
}
