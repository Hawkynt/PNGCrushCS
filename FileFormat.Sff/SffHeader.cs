using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>The 12-byte header at the start of every SFF file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct SffHeader(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 1)] byte Version,
  [property: HeaderField(5, 1)] byte Reserved,
  [property: HeaderField(6, 2)] ushort UserInfoOffset,
  [property: HeaderField(8, 2)] ushort PageCount,
  [property: HeaderField(10, 2)] ushort FirstPageOffset
) {

  public const int StructSize = 12;
  public const byte MagicByte1 = 0x53; // 'S'
  public const byte MagicByte2 = 0x66; // 'f'
  public const byte MagicByte3 = 0x66; // 'f'
  public const byte MagicByte4 = 0x66; // 'f'

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SffHeader>();
}
