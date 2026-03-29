using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 16-byte header at the start of every WPG file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct WpgHeader(
  [property: HeaderField(0, 1)] byte Magic1,
  [property: HeaderField(1, 1)] byte Magic2,
  [property: HeaderField(2, 1)] byte Magic3,
  [property: HeaderField(3, 1)] byte Magic4,
  [property: HeaderField(4, 4)] uint ProductType,
  [property: HeaderField(8, 2)] ushort FileType,
  [property: HeaderField(10, 1)] byte MajorVersion,
  [property: HeaderField(11, 1)] byte MinorVersion,
  [property: HeaderField(12, 2)] ushort EncryptionKey,
  [property: HeaderField(14, 2)] ushort Reserved
) {

  public const int StructSize = 16;
  public const byte MagicByte1 = 0xFF;
  public const byte MagicByte2 = (byte)'W';
  public const byte MagicByte3 = (byte)'P';
  public const byte MagicByte4 = (byte)'C';

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<WpgHeader>();
}
