namespace FileFormat.Bpg;

/// <summary>
/// How a BPG picture's three planes turn into RGB, as the four-bit <c>color_space</c> field spells
/// it.
/// </summary>
/// <remarks>
/// The values are the ones written into the file. <see cref="Rgb"/> is not a colour matrix at all
/// but the absence of one: the planes hold G, B and R in that order, so nothing is transformed on
/// the way out.
/// </remarks>
public enum BpgColorSpace {

  /// <summary>YCbCr with the BT.601 matrix — HEVC <c>matrix_coeffs</c> 5, the one JPEG uses.</summary>
  YCbCrBT601 = 0,

  /// <summary>No transform: the Y plane holds G, the Cb plane B and the Cr plane R.</summary>
  Rgb = 1,

  /// <summary>YCgCo — HEVC <c>matrix_coeffs</c> 8, with Cg in the Cb plane and Co in the Cr plane.</summary>
  YCgCo = 2,

  /// <summary>YCbCr with the BT.709 matrix — HEVC <c>matrix_coeffs</c> 1.</summary>
  YCbCrBT709 = 3,

  /// <summary>YCbCr with the BT.2020 non-constant-luminance matrix — HEVC <c>matrix_coeffs</c> 9.</summary>
  YCbCrBT2020Ncl = 4,

  /// <summary>Reserved for BT.2020 constant luminance, which the format's specification does not define yet.</summary>
  YCbCrBT2020Cl = 5,
}
