using FileFormat.Core;

namespace FileFormat.Fbm;

/// <summary>The 256-byte header of a CMU Fuzzy Bitmap (FBM) file.</summary>
/// <remarks>
/// KNOWN WRONG, and not yet corrected. Every field here is written as a big-endian integer, and the
/// format writes them as fixed-width decimal text instead — XnView says "invalid number of planes"
/// of what this produces, which is it parsing a binary 3 as characters. Probing established that a
/// text header is accepted and that <c>rowlen</c> is the length of one plane's row rather than of
/// all of them together, so the picture is stored plane by plane and not interleaved; what has not
/// been established is where the pixels begin. Probing for it blind did not converge: at every data
/// offset tried, the one tool that reads these renders the picture grey and shows none of the values
/// put in, so the header is wrong somewhere the probe cannot see.
/// <para/>
/// There is no reference to deduce it from either — ImageMagick has no coder for it, the conversion
/// service cannot write one, and the public sample archive has none. Until a real file turns up this
/// stays as it is rather than being guessed at again.
/// <para/>
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
public readonly partial record struct FbmHeader( [property: Repeat(8)] byte[] Magic, int Cols, int Rows, int Bands, int Bits, int PhysBits, int RowLen, int PlnLen, int ClrLen, double Aspect, [property: SeqField(Size = 208)] string Title
) {

 public const int StructSize = 256;

 /// <summary>The 8-byte magic signature including null terminator: "%bitmap\0".</summary>
 public static readonly byte[] MagicBytes = [(byte)'%', (byte)'b', (byte)'i', (byte)'t', (byte)'m', (byte)'a', (byte)'p', 0];

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FbmHeader>();
}
