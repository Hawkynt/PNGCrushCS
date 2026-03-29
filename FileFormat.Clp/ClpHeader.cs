using FileFormat.Core;

namespace FileFormat.Clp;

/// <summary>The 4-byte header at the start of every CLP file.</summary>
[GenerateSerializer]
public readonly partial record struct ClpHeader(
  [property: HeaderField(0, 2)] ushort FileId,
  [property: HeaderField(2, 2)] ushort FormatCount
) {

  public const int StructSize = 4;
  public const ushort FileIdValue = 0xC350;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ClpHeader>();
}
