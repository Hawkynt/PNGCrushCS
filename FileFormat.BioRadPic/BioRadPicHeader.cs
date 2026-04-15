using FileFormat.Core;

namespace FileFormat.BioRadPic;

/// <summary>The 76-byte header at the start of every Bio-Rad PIC file.</summary>
[GenerateSerializer]
[Filler(70, 6)]
public readonly partial record struct BioRadPicHeader( ushort Nx, ushort Ny, ushort Npic, short Ramp1Min, short Ramp1Max, int Notes, short ByteFormat, ushort ImageNumber, string Name, short Merged, ushort Color1, ushort FileId, short Ramp2Min, short Ramp2Max, ushort Color2, short Edited, short Lens, float MagFactor
) {

 /// <summary>The magic value that must appear at offset 54 in a valid Bio-Rad PIC file.</summary>
 public const ushort MagicFileId = 12345;

 public const int StructSize = 76;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<BioRadPicHeader>();
}
