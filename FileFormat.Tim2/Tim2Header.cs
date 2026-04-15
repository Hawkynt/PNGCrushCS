using FileFormat.Core;

namespace FileFormat.Tim2;

/// <summary>The 16-byte TIM2 file header.</summary>
[GenerateSerializer]
[Filler(0, 4)]
[Filler(8, 8)]
public readonly partial record struct Tim2Header( byte Sig0, byte Sig1, byte Sig2, byte Sig3, byte Version, byte Alignment, ushort PictureCount
) {

 public const int StructSize = 16;

 public static readonly byte[] ExpectedSignature = [(byte)'T', (byte)'I', (byte)'M', (byte)'2'];

 public bool IsValid => this.Sig0 == ExpectedSignature[0]
 && this.Sig1 == ExpectedSignature[1]
 && this.Sig2 == ExpectedSignature[2]
 && this.Sig3 == ExpectedSignature[3];

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Tim2Header>();
}
