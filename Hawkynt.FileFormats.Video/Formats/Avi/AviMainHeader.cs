using FileFormat.Core;

namespace FileFormat.Avi;

/// <summary>The <c>avih</c> chunk of an AVI's header list (56 bytes).</summary>
/// <remarks>
/// <see cref="TotalFrames"/> is what the writer believed when it wrote the header, and a file left
/// unfinished keeps whatever was there — so the frame count this reader reports comes from counting
/// the chunks in <c>movi</c>, not from here. The field is kept because it is part of the format and
/// a caller may want to see the two disagree.
/// </remarks>
[GenerateSerializer]
public readonly partial record struct AviMainHeader(
  uint MicroSecondsPerFrame,
  uint MaxBytesPerSecond,
  uint PaddingGranularity,
  uint Flags,
  uint TotalFrames,
  uint InitialFrames,
  uint Streams,
  uint SuggestedBufferSize,
  uint Width,
  uint Height,
  uint Reserved0,
  uint Reserved1,
  uint Reserved2,
  uint Reserved3
) {

  public const int StructSize = 56;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AviMainHeader>();
}
