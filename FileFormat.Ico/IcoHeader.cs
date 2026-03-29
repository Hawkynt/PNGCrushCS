using FileFormat.Core;

namespace FileFormat.Ico;

/// <summary>The 6-byte header at the start of every ICO/CUR file.</summary>
[GenerateSerializer]
internal readonly partial record struct IcoHeader(
  [property: HeaderField(0, 2)] ushort Reserved,
  [property: HeaderField(2, 2)] ushort Type,
  [property: HeaderField(4, 2)] ushort Count
) {

  public const int StructSize = 6;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<IcoHeader>();
}
