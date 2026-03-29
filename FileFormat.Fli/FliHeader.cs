using FileFormat.Core;

namespace FileFormat.Fli;

/// <summary>The 128-byte header at the start of every FLI/FLC file.</summary>
[GenerateSerializer]
[HeaderFiller("Reserved", 20, 108)]
public readonly partial record struct FliHeader(
  [property: HeaderField(0, 4)] int Size,
  [property: HeaderField(4, 2)] short Magic,
  [property: HeaderField(6, 2)] short Frames,
  [property: HeaderField(8, 2)] short Width,
  [property: HeaderField(10, 2)] short Height,
  [property: HeaderField(12, 2)] short Depth,
  [property: HeaderField(14, 2)] short Flags,
  [property: HeaderField(16, 4)] int Speed
) {

  public const int StructSize = 128;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<FliHeader>();
}
