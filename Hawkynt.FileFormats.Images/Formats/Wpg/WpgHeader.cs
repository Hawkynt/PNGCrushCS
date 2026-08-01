using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 16-byte header at the start of every WPG file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct WpgHeader(
  byte Magic1,
  byte Magic2,
  byte Magic3,
  byte Magic4,
  uint DataOffset,
  byte ProductType,
  byte FileType,
  byte MajorVersion,
  byte MinorVersion,
  ushort EncryptionKey,
  ushort Reserved
) {

 public const int StructSize = 16;

 /// <summary>Where the first record sits; sixteen, since the records follow this header.</summary>
 public const uint RecordsOffset = 16;

 /// <summary>The product a file was written by. One is WordPerfect itself.</summary>
 public const byte WordPerfect = 1;

 /// <summary>The file type byte that says the records hold a graphic.</summary>
 public const byte GraphicFileType = 0x16;
 public const byte MagicByte1 = 0xFF;
 public const byte MagicByte2 = (byte)'W';
 public const byte MagicByte3 = (byte)'P';
 public const byte MagicByte4 = (byte)'C';

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WpgHeader>();
}
