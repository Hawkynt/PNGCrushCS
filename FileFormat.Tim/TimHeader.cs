using FileFormat.Core;

namespace FileFormat.Tim;

/// <summary>The 8-byte TIM file header: 4-byte magic (0x10) + 4-byte flags.</summary>
[GenerateSerializer]
public readonly partial record struct TimHeader(
  [property: HeaderField(0, 4)] uint Magic,
  [property: HeaderField(4, 4)] uint Flags
) {

  public const int StructSize = 8;
  public const uint ExpectedMagic = 0x10;

  /// <summary>Bits per pixel mode extracted from flags bits 0-1.</summary>
  public TimBpp Bpp => (TimBpp)(this.Flags & 0x03);

  /// <summary>Whether a CLUT (palette) is present, extracted from flags bit 3.</summary>
  public bool HasClut => (this.Flags & 0x08) != 0;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<TimHeader>();
}
