using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>The 88-byte EMR_HEADER record at the start of every EMF file.</summary>
[GenerateSerializer]
[Filler(60, 28)]
public readonly partial record struct EmfHeaderRecord( uint RecordType, uint RecordSize, int BoundsLeft, int BoundsTop, int BoundsRight, int BoundsBottom, int FrameLeft, int FrameTop, int FrameRight, int FrameBottom, uint Signature, uint Version, uint FileSize, uint RecordCount, ushort NumHandles, ushort Reserved
) {

 public const int StructSize = 88;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<EmfHeaderRecord>();
}
