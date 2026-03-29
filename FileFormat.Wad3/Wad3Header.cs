using FileFormat.Core;

namespace FileFormat.Wad3;

/// <summary>The 12-byte header at the start of every WAD3 file.</summary>
[GenerateSerializer]
[HeaderFiller("Magic", 0, 4)]
public readonly partial record struct Wad3Header(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 4)] int NumLumps,
  [property: HeaderField(8, 4)] int DirectoryOffset
) {

  public const int StructSize = 12;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Wad3Header>();
}
