using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 12-byte table-of-contents entry (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorTocEntry(
  [property: HeaderField(0, 4)] uint Type,
  [property: HeaderField(4, 4)] uint Subtype,
  [property: HeaderField(8, 4)] uint Position
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<XcursorTocEntry>();
}
