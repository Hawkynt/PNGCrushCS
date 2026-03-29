using FileFormat.Core;

namespace FileFormat.DrHalo;

/// <summary>The 6-byte header at the start of every Dr. Halo CUT file.</summary>
[GenerateSerializer]
public readonly partial record struct DrHaloHeader(
  [property: HeaderField(0, 2)] short Width,
  [property: HeaderField(2, 2)] short Height,
  [property: HeaderField(4, 2)] short Reserved
) {

  public const int StructSize = 6;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DrHaloHeader>();
}
