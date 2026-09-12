namespace FileFormat.Gif;

/// <summary>Encoder knobs for <see cref="GifWriter"/> and <see cref="GifStreamWriter"/>. Purely a
/// writer-side choice: every option produces a stream any conforming decoder reads back identically.</summary>
public sealed record GifWriteOptions {

  /// <summary>How hard to compress. Defaults to <see cref="GifCompressionLevel.Standard"/>.</summary>
  public GifCompressionLevel Compression { get; init; } = GifCompressionLevel.Standard;

  /// <summary>Keep using a full dictionary as a static codebook instead of clearing it immediately.
  /// Only meaningful for <see cref="GifCompressionLevel.Standard"/> — it can win noticeably on
  /// gradients and lose just as badly on banded content, which is what
  /// <see cref="GifCompressionLevel.Best"/> exists to decide per frame.</summary>
  public bool DeferClear { get; init; }

  /// <summary>Standard LZW, immediate clear.</summary>
  public static GifWriteOptions Default { get; } = new();

  /// <summary>Literal-code output with no dictionary matching.</summary>
  public static GifWriteOptions NoCompression { get; } = new() { Compression = GifCompressionLevel.None };

  /// <summary>Try every variant and keep the smallest frame.</summary>
  public static GifWriteOptions SmallestOutput { get; } = new() { Compression = GifCompressionLevel.Best };
}
