using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Shared type contracts for the modular sub-codec (ISO/IEC 18181-1 §H, libjxl
// `lib/jxl/modular/`). These types form the integration boundary between
// MA-tree decoding, weighted-predictor state, transform inverse, and the
// top-level channel-iteration loop. Each piece can be implemented independently
// against this contract.
// =====================================================================================

/// <summary>A single logical channel of modular pixel data. After all transforms
/// are inverted, the modular image consists of one or more channels — for a Gray
/// image, just one; for RGB after RCT inverse, three; for an alpha-bearing image,
/// add one more. Subsample shifts handle the subsampled chroma case.</summary>
internal sealed class JxlChannel {
  public required int Width { get; init; }
  public required int Height { get; init; }
  /// <summary>log2 horizontal subsample (0 = full resolution).</summary>
  public int HShift { get; init; }
  /// <summary>log2 vertical subsample (0 = full resolution).</summary>
  public int VShift { get; init; }
  /// <summary>Row-major pixel data, length = Width * Height.</summary>
  public required int[] Pixels { get; init; }

  public int Get(int x, int y) => Pixels[y * Width + x];
  public void Set(int x, int y, int v) => Pixels[y * Width + x] = v;
}

/// <summary>Result of decoding a modular image: the list of channels after all
/// transforms have been inverted. Channels are in canonical color order
/// (Y or R-G-B, then alpha, then any extras).</summary>
/// <summary>
/// A picture assembled from several frames, already blended and already in
/// float, one plane per channel.
/// </summary>
/// <remarks>
/// A multi-frame file cannot come back as its last frame's samples, because
/// blending happens between them at a precision samples do not have. What comes
/// back is the composition, and it is rounded once on the way out.
/// </remarks>
internal sealed class JxlComposedImage {
  public required int Width { get; init; }
  public required int Height { get; init; }

  /// <summary>Three colour planes followed by the extra channels, each a
  /// fraction of full scale.</summary>
  public required float[][] Planes { get; init; }

  /// <summary>Which plane is the alpha, or -1 for none.</summary>
  public required int AlphaPlane { get; init; }
}

internal sealed class JxlModularImage {
  public required JxlChannel[] Channels { get; init; }

  /// <summary>
  /// The three colour planes as fractions of full scale, present only when the
  /// frame carried something that had to be drawn on top of the samples rather
  /// than coded into them — splines, so far.
  /// </summary>
  /// <remarks>
  /// A spline states a colour that is not a whole sample, so the picture has to
  /// be finished in floats and rounded once at the end. When this is set it is
  /// the picture, and <see cref="Channels"/> holds what was there before the
  /// drawing.
  /// </remarks>
  public float[][]? ColorPlanes { get; set; }
}

/// <summary>One node of the meta-adaptive (MA) decision tree (ISO/IEC 18181-1 §H.2).
/// Inner nodes split on a property comparison; leaves carry a prediction
/// configuration (predictor index, offset, multiplier, context).</summary>
internal sealed class JxlMaTreeNode {
  /// <summary>For inner nodes: which property to compare. -1 for leaves.</summary>
  public int PropertyIndex { get; init; } = -1;
  /// <summary>For inner nodes: the threshold; the left subtree is taken when
  /// property &gt; threshold (libjxl convention).</summary>
  public int Threshold { get; init; }
  public JxlMaTreeNode? Left { get; init; }
  public JxlMaTreeNode? Right { get; init; }

  // Leaf-only fields:
  public int LeafPredictor { get; init; }
  public int LeafOffset { get; init; }
  public int LeafMultiplier { get; init; }
  /// <summary>The entropy-decoder context to use for residuals at this leaf.
  /// Each leaf gets a unique context, numbered in left-to-right traversal order.</summary>
  public int LeafContext { get; init; }

  public bool IsLeaf => Left == null;
}

/// <summary>The meta-adaptive tree as a flat list (suitable for serialization/inspection)
/// plus the root for traversal.</summary>
internal sealed class JxlMaTree {
  public required JxlMaTreeNode Root { get; init; }
  public required int LeafCount { get; init; }

  /// <summary>Walk the tree to find the leaf for the given property vector.
  /// Returns the leaf node (caller reads predictor/offset/multiplier/context off it).</summary>
  public JxlMaTreeNode Traverse(int[] properties) {
    var node = Root;
    while (!node.IsLeaf)
      node = properties[node.PropertyIndex] > node.Threshold ? node.Left! : node.Right!;
    return node;
  }
}

/// <summary>Modular transform types (ISO/IEC 18181-1 §H.5). The transform chain
/// is applied in encode-order during encoding; the decoder inverts them in reverse.</summary>
internal enum JxlModularTransformType : byte {
  Rct = 0,      // Reversible Color Transform (42 variants)
  Palette = 1,  // Indexed-color reduction
  Squeeze = 2,  // Wavelet-like haar transform
}

/// <summary>Parsed parameters of a single modular transform. Sub-type-specific fields
/// vary; we use a flat record with optional fields per the variant. The bitstream
/// reader populates this from the bitstream; the inverse is applied to channels.</summary>
internal sealed class JxlModularTransform {
  public required JxlModularTransformType Type { get; init; }

  // RCT-specific:
  public int RctBeginC { get; init; }    // first channel involved
  public int RctType { get; init; }      // 0..41 (which of the 42 RCT variants)

  // Palette-specific:
  public int PaletteBeginC { get; init; }
  public int PaletteNumC { get; init; }  // number of channels collapsed into the palette
  public int PaletteSize { get; init; }  // entries in the palette (≤ 1024)
  public int PaletteDeltaPredictor { get; init; }
  public int[] PaletteData { get; set; } = [];

  // Squeeze-specific:
  public JxlSqueezeStep[] SqueezeSteps { get; init; } = [];
}

/// <summary>One squeeze step parameter (channels begin/num + horizontal flag).</summary>
internal readonly record struct JxlSqueezeStep(int BeginC, int NumC, bool Horizontal, bool InPlace);
