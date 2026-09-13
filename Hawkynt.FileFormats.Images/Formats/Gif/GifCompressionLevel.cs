namespace FileFormat.Gif;

/// <summary>How hard <see cref="GifWriter"/> works at compressing frame pixel data.</summary>
public enum GifCompressionLevel {
  /// <summary>Emit every pixel as its own literal LZW code — no dictionary matching at all.
  /// Still a spec-legal LZW stream (the code width is pinned by clearing the dictionary before the
  /// decoder can grow it), so any decoder reads it; it is simply much larger. Useful when the file
  /// is about to be recompressed by something else, and for writer/decoder differential testing.</summary>
  None,
  /// <summary>Greedy variable-width LZW with a clear on dictionary overflow. The default, and what
  /// every other GIF encoder does.</summary>
  Standard,
  /// <summary>Run every Standard variant plus a dynamic-programming optimal code selection and keep
  /// the smallest result. Costs roughly three encodes; wins a few percent on real frames.</summary>
  Best,
}
