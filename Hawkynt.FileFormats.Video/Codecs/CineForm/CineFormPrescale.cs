using System;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The prescale shifts a real CineForm encoder applies at each wavelet level, indexed by bit depth.
/// </summary>
/// <remarks>
/// SMPTE ST 2073-1:2017, Annex E.1 (Table E.1) gives shifts an encoder "can benefit from" when
/// <c>BitsPerComponent</c> is twelve bits or less — not a requirement, and not something a conformant
/// bitstream has to restate, since <c>PrescaleShift</c> (tag 109) defaults to (0,0,0) per Table B.2 and
/// stays that way unless the tag says otherwise. No file measured against ffmpeg's own <c>cfhd</c>
/// encoder carries tag 109 at all.
/// <para/>
/// <b>The twelve-bit shifts are Table E.1's own, unchanged, and confirmed by full reconstruction.</b>
/// A <c>gbrp12le</c> channel decoded with (0,2,2) and compared against ffmpeg's own raw decode of the
/// same frame, sample by sample, differs by at most 34 of 4095 with a mean of 3 — the residual
/// quantisation leaves, not a structural error.
/// <para/>
/// <b>The ten-bit shifts are not Table E.1's.</b> Table E.1 states (0,0,2) for ten bits — the shift
/// applied at wavelet level 3, the coarsest. Applying that literally reconstructs a <c>yuv422p10le</c>
/// channel with the right lowpass and the right level-1 and level-3 highpass, and level 2's highpass
/// four times too large: forward-transforming ffmpeg's own decoded reference through the same three
/// levels and comparing each subband's pre-quantisation coefficients against what this decoder read
/// back (dequantised, undoing the codebook's companding curve) shows level 2 short by a factor of
/// exactly four — 2², the level-3 shift Table E.1 states — while levels 1 and 3 already agree to within
/// single digits. Moving that shift from level 3 to level 2 — (0,2,0) — removes the factor of four
/// everywhere it appeared and brings the whole channel, lowpass and all nine highpass subbands, within
/// single digits of the forward-transformed reference; the full picture then matches ffmpeg's own
/// decode with a mean difference of well under one level across every sample, where (0,0,2) read a
/// mean difference of forty-nine. Table E.1's own two shifts of two are not swapped for the twelve-bit
/// case because it states the same value at both levels, so nothing about the twelve-bit result changes
/// under the same correction — which is exactly why the ten-bit half was the one measured wrong for as
/// long as only a flat frame was checked: a flat frame carries no highpass energy at any level, so
/// swapping which of two equal-sized shifts lands on which level of a flat channel cannot be told apart
/// by their reconstructed value, and only real, moving content exposes the difference.
/// </remarks>
internal static class CineFormPrescale {

  /// <summary>PrescaleShift[0..2] for a ten-bit stream — <c>yuv422p10le</c>, the depth ffmpeg's
  /// <c>cfhd</c> encoder codes 4:2:2 at.</summary>
  internal static ReadOnlySpan<int> TenBit => [0, 2, 0];

  /// <summary>PrescaleShift[0..2] for a twelve-bit stream — <c>gbrp12le</c> and <c>gbrap12le</c>, the
  /// depth ffmpeg's <c>cfhd</c> encoder codes RGB at.</summary>
  internal static ReadOnlySpan<int> TwelveBit => [0, 2, 2];
}
