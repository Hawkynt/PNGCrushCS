using FileFormat.Core;

namespace FileFormat.Apng;

/// <summary>The 26-byte fcTL (Frame Control) chunk data in an APNG file.</summary>
[GenerateSerializer]
public readonly partial record struct ApngFctl(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int SequenceNumber,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int Width,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] int Height,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int XOffset,
  [property: HeaderField(16, 4, Endianness = Endianness.Big)] int YOffset,
  [property: HeaderField(20, 2, Endianness = Endianness.Big)] ushort DelayNum,
  [property: HeaderField(22, 2, Endianness = Endianness.Big)] ushort DelayDen,
  [property: HeaderField(24, 1)] byte DisposeOp,
  [property: HeaderField(25, 1)] byte BlendOp
) {

  public const int StructSize = 26;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ApngFctl>();
}
