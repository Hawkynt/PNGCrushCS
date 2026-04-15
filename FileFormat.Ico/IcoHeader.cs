using FileFormat.Core;

namespace FileFormat.Ico;

/// <summary>The 6-byte header at the start of every ICO/CUR file.</summary>
[GenerateSerializer]
internal readonly partial record struct IcoHeader( ushort Reserved, ushort Type, ushort Count
) {

 public const int StructSize = 6;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<IcoHeader>();
}
