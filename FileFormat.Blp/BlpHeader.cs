using FileFormat.Core;

namespace FileFormat.Blp;

/// <summary>The 20-byte fixed header at the start of every BLP2 file:
/// Magic (uint32 LE), Type (uint32 LE), Encoding (byte), AlphaDepth (byte),
/// AlphaEncoding (byte), HasMips (byte), Width (uint32 LE), Height (uint32 LE).</summary>
[GenerateSerializer]
internal readonly partial record struct BlpHeader(
  uint Magic,
  uint Type,
  byte Encoding,
  byte AlphaDepth,
  byte AlphaEncoding,
  byte HasMips,
  uint Width,
  uint Height
) {
  public const int StructSize = 20;
}
