using FileFormat.Core;

namespace FileFormat.Bsave;

/// <summary>The 7-byte header at the start of every BSAVE file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct BsaveHeader( byte Magic, ushort Segment, ushort Offset, ushort Length
) {

 public const int StructSize = 7;
 public const byte MagicValue = 0xFD;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BsaveHeader>();
}
