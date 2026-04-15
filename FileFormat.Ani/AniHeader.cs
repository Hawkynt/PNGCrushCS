using FileFormat.Core;

namespace FileFormat.Ani;

/// <summary>ANI header data from the 'anih' chunk (36 bytes).</summary>
[GenerateSerializer]
public readonly partial record struct AniHeader(
  int CbSize,
  int NumFrames,
  int NumSteps,
  int Width,
  int Height,
  int BitCount,
  int NumPlanes,
  int DisplayRate,
  int Flags
) {

 public const int StructSize = 36;

 /// <summary>Whether the ANI has a sequence chunk (flag bit 0).</summary>
 public bool HasSequence => (this.Flags & 1) != 0;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AniHeader>();
}
