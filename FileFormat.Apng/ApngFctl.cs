using FileFormat.Core;

namespace FileFormat.Apng;

/// <summary>The 26-byte fcTL (Frame Control) chunk data in an APNG file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct ApngFctl(
  int SequenceNumber,
  int Width,
  int Height,
  int XOffset,
  int YOffset,
  ushort DelayNum,
  ushort DelayDen,
  byte DisposeOp,
  byte BlendOp
) {

 public const int StructSize = 26;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<ApngFctl>();
}
