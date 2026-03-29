using FileFormat.Core;

namespace FileFormat.Wad;

/// <summary>The 12-byte header at the start of every WAD file.</summary>
[GenerateSerializer]
[HeaderFiller("Identification", 0, 4)]
public readonly partial record struct WadHeader(
  [property: HeaderField(0, 1)] byte Id1,
  [property: HeaderField(1, 1)] byte Id2,
  [property: HeaderField(2, 1)] byte Id3,
  [property: HeaderField(3, 1)] byte Id4,
  [property: HeaderField(4, 4)] int NumLumps,
  [property: HeaderField(8, 4)] int DirectoryOffset
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WadHeader>();
}
