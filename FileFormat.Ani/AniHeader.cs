using FileFormat.Core;

namespace FileFormat.Ani;

/// <summary>ANI header data from the 'anih' chunk (36 bytes).</summary>
[GenerateSerializer]
public readonly partial record struct AniHeader(
  [property: HeaderField(0, 4)] int CbSize,
  [property: HeaderField(4, 4)] int NumFrames,
  [property: HeaderField(8, 4)] int NumSteps,
  [property: HeaderField(12, 4)] int Width,
  [property: HeaderField(16, 4)] int Height,
  [property: HeaderField(20, 4)] int BitCount,
  [property: HeaderField(24, 4)] int NumPlanes,
  [property: HeaderField(28, 4)] int DisplayRate,
  [property: HeaderField(32, 4)] int Flags
) {

  public const int StructSize = 36;

  /// <summary>Whether the ANI has a sequence chunk (flag bit 0).</summary>
  public bool HasSequence => (this.Flags & 1) != 0;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AniHeader>();
}
