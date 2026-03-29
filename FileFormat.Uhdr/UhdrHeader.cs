using FileFormat.Core;

namespace FileFormat.Uhdr;

/// <summary>The 16-byte header of a UHDR image: Magic(4 bytes "UHDR"), Version(ushort LE), Reserved(ushort LE), Width(uint32 LE), Height(uint32 LE).</summary>
[GenerateSerializer]
public readonly partial record struct UhdrHeader(
  [property: HeaderField(0, 4, Name = "Magic")] string Magic,
  [property: HeaderField(4, 2)] ushort Version,
  [property: HeaderField(6, 2)] ushort Reserved,
  [property: HeaderField(8, 4)] uint Width,
  [property: HeaderField(12, 4)] uint Height
) {

  public const int StructSize = 16;
  public const string MagicValue = "UHDR";
  public const ushort CurrentVersion = 1;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<UhdrHeader>();
}
