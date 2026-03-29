using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>Xcursor 16-byte file header (LE).</summary>
[GenerateSerializer]
public readonly partial record struct XcursorFileHeader(
  [property: HeaderField(0, 4)] uint Magic,
  [property: HeaderField(4, 4)] uint HeaderSize,
  [property: HeaderField(8, 4)] uint Version,
  [property: HeaderField(12, 4)] uint TocCount
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<XcursorFileHeader>();
}
