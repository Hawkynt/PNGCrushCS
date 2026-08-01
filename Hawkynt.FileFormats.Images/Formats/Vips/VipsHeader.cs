using FileFormat.Core;

namespace FileFormat.Vips;

/// <summary>The 64-byte VIPS native image file header.</summary>
[GenerateSerializer]
public readonly partial record struct VipsHeader(
  int Magic,
  int Width,
  int Height,
  int Bands,
  int Unused1,
  int BandFormat,
  int Coding,
  int Type,
  float XRes,
  float YRes,
  int XOffset,
  int YOffset,
  int Length,
  short Compression,
  short Level,
  int BBits,
  int Unused2
) {

 /// <summary>Expected magic value for VIPS native images: 0x08F2A6B6.</summary>
 public const int MagicValue = unchecked((int)0x08F2A6B6);

 /// <summary>The same magic written the other way round, which is how the format names its order.</summary>
 /// <remarks>
 /// VIPS wrote its header in whichever order the machine used and left the magic to say which —
 /// the two spellings are the same four bytes reversed. A file carrying this one has every field
 /// reversed too, not only the magic.
 /// </remarks>
 public const int SwappedMagicValue = unchecked((int)0xB6A6F208);

 public const int StructSize = 64;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<VipsHeader>();
}
