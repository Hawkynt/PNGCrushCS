using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 16-byte header at the start of every WPG file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct WpgHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  uint ProductType,
  ushort FileType,
  byte MajorVersion,
  byte MinorVersion,
  ushort EncryptionKey,
  ushort Reserved
) {

 public const int StructSize = 16;
 public const byte MagicByte1 = 0xFF;
 public const byte MagicByte2 = (byte)'W';
 public const byte MagicByte3 = (byte)'P';
 public const byte MagicByte4 = (byte)'C';

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WpgHeader>();
}
