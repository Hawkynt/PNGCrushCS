using FileFormat.Core;

namespace FileFormat.Tim2;

/// <summary>The 16-byte TIM2 file header.</summary>
[GenerateSerializer]
[HeaderFiller("Signature", 0, 4)]
[HeaderFiller("Padding", 8, 8)]
public readonly partial record struct Tim2Header(
  [property: HeaderField(0, 1)] byte Sig0,
  [property: HeaderField(1, 1)] byte Sig1,
  [property: HeaderField(2, 1)] byte Sig2,
  [property: HeaderField(3, 1)] byte Sig3,
  [property: HeaderField(4, 1)] byte Version,
  [property: HeaderField(5, 1)] byte Alignment,
  [property: HeaderField(6, 2)] ushort PictureCount
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
