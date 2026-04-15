using FileFormat.Core;

namespace FileFormat.Fbm;

/// <summary>The 256-byte header of a CMU Fuzzy Bitmap (FBM) file.</summary>
/// <remarks>
/// Layout:
/// 0-7: magic "%bitmap\0" (8 bytes)
/// 8-11: cols (int32 BE)
/// 12-15: rows (int32 BE)
/// 16-19: bands (int32 BE)
/// 20-23: bits (int32 BE)
/// 24-27: physbits (int32 BE)
/// 28-31: rowlen (int32 BE)
/// 32-35: plnlen (int32 BE)
/// 36-39: clrlen (int32 BE)
/// 40-47: aspect (double BE)
/// 48-255: title (null-terminated ASCII, zero-padded)
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(48, 208)]
public readonly partial record struct FbmHeader( byte[] Magic, int Cols, int Rows, int Bands, int Bits, int PhysBits, int RowLen, int PlnLen, int ClrLen, double Aspect, string Title
) {

 public const int StructSize = 256;

 /// <summary>The 8-byte magic signature including null terminator: "%bitmap\0".</summary>
 public static readonly byte[] MagicBytes = [(byte)'%', (byte)'b', (byte)'i', (byte)'t', (byte)'m', (byte)'a', (byte)'p', 0];

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FbmHeader>();
}
