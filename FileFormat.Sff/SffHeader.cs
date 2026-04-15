using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>The 12-byte header at the start of every SFF file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct SffHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  byte Version,
  byte Reserved,
  ushort UserInfoOffset,
  ushort PageCount,
  ushort FirstPageOffset
) {

 public const int StructSize = 12;
 public const byte MagicByte1 = 0x53; // 'S'
 public const byte MagicByte2 = 0x66; // 'f'
 public const byte MagicByte3 = 0x66; // 'f'
 public const byte MagicByte4 = 0x66; // 'f'

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SffHeader>();
}
