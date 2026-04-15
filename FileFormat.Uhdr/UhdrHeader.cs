using FileFormat.Core;

namespace FileFormat.Uhdr;

/// <summary>The 16-byte header of a UHDR image: Magic(4 bytes "UHDR"), Version(ushort LE), Reserved(ushort LE), Width(uint32 LE), Height(uint32 LE).</summary>
[GenerateSerializer]
public readonly partial record struct UhdrHeader(
  string Magic,
  ushort Version,
  ushort Reserved,
  uint Width,
  uint Height
) {

 public const int StructSize = 16;
 public const string MagicValue = "UHDR";
 public const ushort CurrentVersion = 1;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<UhdrHeader>();
}
