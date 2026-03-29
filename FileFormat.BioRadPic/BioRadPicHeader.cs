using FileFormat.Core;

namespace FileFormat.BioRadPic;

/// <summary>The 76-byte header at the start of every Bio-Rad PIC file.</summary>
[GenerateSerializer]
[HeaderFiller("reserved", 70, 6)]
public readonly partial record struct BioRadPicHeader(
  [property: HeaderField(0, 2)] ushort Nx,
  [property: HeaderField(2, 2)] ushort Ny,
  [property: HeaderField(4, 2)] ushort Npic,
  [property: HeaderField(6, 2)] short Ramp1Min,
  [property: HeaderField(8, 2)] short Ramp1Max,
  [property: HeaderField(10, 4)] int Notes,
  [property: HeaderField(14, 2)] short ByteFormat,
  [property: HeaderField(16, 2)] ushort ImageNumber,
  [property: HeaderField(18, 32)] string Name,
  [property: HeaderField(50, 2)] short Merged,
  [property: HeaderField(52, 2)] ushort Color1,
  [property: HeaderField(54, 2)] ushort FileId,
  [property: HeaderField(56, 2)] short Ramp2Min,
  [property: HeaderField(58, 2)] short Ramp2Max,
  [property: HeaderField(60, 2)] ushort Color2,
  [property: HeaderField(62, 2)] short Edited,
  [property: HeaderField(64, 2)] short Lens,
  [property: HeaderField(66, 4)] float MagFactor
) {

  /// <summary>The magic value that must appear at offset 54 in a valid Bio-Rad PIC file.</summary>
  public const ushort MagicFileId = 12345;

  public const int StructSize = 76;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<BioRadPicHeader>();
}
