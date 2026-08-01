using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 16-byte header at the start of every WPG file. All multi-byte fields are little-endian.</summary>
/// <remarks>
/// The four bytes after the magic are where the first record starts, and they were not modelled at
/// all: a four-byte product type sat in their place, pushing the one-byte product type and file type
/// into the version bytes. Every file this wrote therefore said its records began at byte 1, and
/// named itself file type 0 rather than the 0x16 that means a graphic — which is a file no reader
/// will open.
/// </remarks>
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
 public const byte MagicByte1 = 0xFF;
 public const byte MagicByte2 = (byte)'W';
 public const byte MagicByte3 = (byte)'P';
 public const byte MagicByte4 = (byte)'C';

 /// <summary>The file type that means "a graphic", as opposed to a document or a macro.</summary>
 public const byte GraphicFileType = 0x16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WpgHeader>();
}
