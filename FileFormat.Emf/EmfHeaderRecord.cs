using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>The 88-byte EMR_HEADER record at the start of every EMF file.</summary>
[GenerateSerializer]
[HeaderFiller("Unused", 60, 28)]
public readonly partial record struct EmfHeaderRecord(
  [property: HeaderField(0, 4)] uint RecordType,
  [property: HeaderField(4, 4)] uint RecordSize,
  [property: HeaderField(8, 4)] int BoundsLeft,
  [property: HeaderField(12, 4)] int BoundsTop,
  [property: HeaderField(16, 4)] int BoundsRight,
  [property: HeaderField(20, 4)] int BoundsBottom,
  [property: HeaderField(24, 4)] int FrameLeft,
  [property: HeaderField(28, 4)] int FrameTop,
  [property: HeaderField(32, 4)] int FrameRight,
  [property: HeaderField(36, 4)] int FrameBottom,
  [property: HeaderField(40, 4)] uint Signature,
  [property: HeaderField(44, 4)] uint Version,
  [property: HeaderField(48, 4)] uint FileSize,
  [property: HeaderField(52, 4)] uint RecordCount,
  [property: HeaderField(56, 2)] ushort NumHandles,
  [property: HeaderField(58, 2)] ushort Reserved
) {

  public const int StructSize = 88;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<EmfHeaderRecord>();
}
