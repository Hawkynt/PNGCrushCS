using FileFormat.Core;

namespace FileFormat.Wad;

/// <summary>A 16-byte directory entry within a WAD file.</summary>
[GenerateSerializer]
public readonly partial record struct WadEntry(
  [property: HeaderField(0, 4)] int FilePos,
  [property: HeaderField(4, 4)] int Size,
  [property: HeaderField(8, 8)] string Name
) {

  public const int StructSize = 16;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WadEntry>();
}
