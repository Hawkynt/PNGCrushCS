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
  /// <summary>Gets the micro Seconds Per Frame.</summary>
  public uint MicroSecondsPerFrame { get; init; } = MicroSecondsPerFrame;
  /// <summary>Gets the max Bytes Per Second.</summary>
  public uint MaxBytesPerSecond { get; init; } = MaxBytesPerSecond;
  /// <summary>Gets the padding Granularity.</summary>
  public uint PaddingGranularity { get; init; } = PaddingGranularity;
  /// <summary>Gets the flags.</summary>
  public uint Flags { get; init; } = Flags;
  /// <summary>Gets the total Frames.</summary>
  public uint TotalFrames { get; init; } = TotalFrames;
  /// <summary>Gets the initial Frames.</summary>
  public uint InitialFrames { get; init; } = InitialFrames;
  /// <summary>Gets the streams.</summary>
  public uint Streams { get; init; } = Streams;
  /// <summary>Gets the suggested Buffer Size.</summary>
  public uint SuggestedBufferSize { get; init; } = SuggestedBufferSize;
  /// <summary>Gets the width.</summary>
  public uint Width { get; init; } = Width;
  /// <summary>Gets the height.</summary>
  public uint Height { get; init; } = Height;
  /// <summary>Gets the reserved0.</summary>
  public uint Reserved0 { get; init; } = Reserved0;
  /// <summary>Gets the reserved1.</summary>
  public uint Reserved1 { get; init; } = Reserved1;
  /// <summary>Gets the reserved2.</summary>
  public uint Reserved2 { get; init; } = Reserved2;
  /// <summary>Gets the reserved3.</summary>
  public uint Reserved3 { get; init; } = Reserved3;

  /// <summary>The serialized structure size, in bytes.</summary>
  public const int StructSize = 56;

  /// <summary>Gets descriptors for the serialized fields.</summary>
  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AviMainHeader>();
}
