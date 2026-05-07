using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Shared type contracts for the VarDCT sub-codec (ISO/IEC 18181-1 §G, libjxl
// `lib/jxl/dec_group.cc`, `lib/jxl/dec_ans.cc`, `lib/jxl/quant_weights.cc`,
// `lib/jxl/dec_xyb.cc`). VarDCT is JXL's lossy mode — perceptually-tuned XYB
// color encoding + variable-size DCT (2×2 through 256×256) + quantization with
// 4 selectable tables + EPF/Gaborish loop filters + patches + splines.
//
// This file establishes the integration boundary. Sub-pieces (DCT inverse,
// dequantization, XYB→RGB, AC strategy, group decoder) build against these types.
// =====================================================================================

/// <summary>VarDCT block-shape strategies (ISO/IEC 18181-1 §G.5, "AC Strategy").
/// 38+ variants total; first-wave implementation may cover only DCT8 (the most
/// common case). Numbering matches libjxl's <c>AcStrategy::Type</c> enum.</summary>
internal enum JxlAcStrategyType : byte {
  Dct8x8 = 0,
  Hornuss = 1,           // 8×8 special: predicted DC only
  Dct2x2 = 2,
  Dct4x4 = 3,
  Dct16x16 = 4,
  Dct32x32 = 5,
  Dct16x8 = 6,
  Dct8x16 = 7,
  Dct32x8 = 8,
  Dct8x32 = 9,
  Dct32x16 = 10,
  Dct16x32 = 11,
  Dct4x8 = 12,
  Dct8x4 = 13,
  Afv0 = 14,             // Asymmetric / fovea variants — 2×2 corners
  Afv1 = 15,
  Afv2 = 16,
  Afv3 = 17,
  Dct64x64 = 18,
  Dct64x32 = 19,
  Dct32x64 = 20,
  Dct128x128 = 21,
  Dct128x64 = 22,
  Dct64x128 = 23,
  Dct256x256 = 24,
  Dct256x128 = 25,
  Dct128x256 = 26,
}

/// <summary>JXL channel index inside an XYB-encoded VarDCT frame. Y/X-coding
/// and Cb/Cr-like roles are non-trivial; libjxl uses a "DCT channel order" that
/// matches the bitstream layout.</summary>
internal enum JxlVarDctChannel : byte {
  X = 0,   // approximately red-minus-green
  Y = 1,   // approximately green (the luminance-like channel)
  B = 2,   // approximately blue-minus-yellow
}

/// <summary>An 8×8 DCT block of transform coefficients. Stored in scan order
/// (zigzag-equivalent for DCT8 — the spec calls it "natural"). Coefficient
/// values are quantized integers; dequantization multiplies by the appropriate
/// quant-table entry.</summary>
internal sealed class JxlDctBlock {
  /// <summary>Block width × height (per AC strategy). Not always 8×8.</summary>
  public required int Width { get; init; }
  public required int Height { get; init; }
  /// <summary>Scanned coefficients, length = Width × Height.</summary>
  public required short[] Coefficients { get; init; }
}

/// <summary>JXL has 4 selectable quantization tables per channel. Each table
/// is 8×8 (for DCT8 blocks); larger blocks use scaled / repeated entries.
/// Default values come from libjxl <c>kDefaultQuantWeights</c> in
/// <c>quant_weights.cc</c>.</summary>
internal sealed class JxlQuantTable {
  public required int Width { get; init; }     // 8 for DCT8
  public required int Height { get; init; }
  public required float[] Weights { get; init; } // length = Width * Height
}

/// <summary>The set of 4 quant-table presets for a frame, indexed [presetIdx][channel].
/// In a full encoder this is per-block-shape, but for first-wave DCT8-only
/// the default table from libjxl is sufficient.</summary>
internal sealed class JxlQuantTableSet {
  public required JxlQuantTable[] Tables { get; init; } // length = numChannels (3 for XYB)
}

/// <summary>An LF (low-frequency) coefficient block. For DCT8, this is the 1
/// DC coefficient per block (the average). For larger DCT shapes (DCT16+),
/// the LF block is the lower-resolution coefficient sub-image.</summary>
internal sealed class JxlLfBlock {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required short[] Coefficients { get; init; } // [y * Width + x]
}

/// <summary>One full VarDCT group. JXL splits the image into "groups" (spatial
/// blocks of e.g. 256×256 pixels). Each group has independent entropy-coded
/// AC/LF data, but shares the frame's quantization + AC strategy.</summary>
internal sealed class JxlVarDctGroup {
  public required int X { get; init; }       // top-left in image-pixel coords
  public required int Y { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  /// <summary>Per-channel AC blocks (decoded coefficients in scan order).</summary>
  public required JxlDctBlock[][] AcBlocks { get; init; }   // [channel][block]
  /// <summary>Per-channel LF coefficient sub-image.</summary>
  public required JxlLfBlock[] LfBlocks { get; init; }      // length = numChannels
}

/// <summary>Decoded VarDCT image — 3 XYB-channel planes after IDCT but before
/// XYB→RGB conversion. The orchestrator's final step calls
/// <see cref="JxlXybColorTransform"/> to produce sRGB pixels.</summary>
internal sealed class JxlVarDctImage {
  public required int Width { get; init; }
  public required int Height { get; init; }
  /// <summary>Per-channel float pixel data, length = Width * Height.</summary>
  public required float[][] Channels { get; init; } // [channel][y * Width + x]
}
