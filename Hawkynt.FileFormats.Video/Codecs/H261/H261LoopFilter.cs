using System;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The optional spatial filter of ITU-T H.261 clause 3.2.3, which is part of prediction and not a
/// post-decode step.
/// </summary>
/// <remarks>
/// This is the one place H.261 and every codec after it in this library part company. H.263 has no
/// loop filter in baseline mode at all; the later standards this package reads that do have one — VP8,
/// VP9 — run it on the finished, reconstructed picture, after the residual has already been added, so
/// that what a later picture predicts from is the filtered result. H.261's filter runs on the
/// <b>prediction</b> itself, before the residual is added to it: clause 3.2.3 says it "operates on pels
/// within a predicted 8 by 8 block", and Figure 3's block diagram draws it (F) reading from the picture
/// memory (P) and feeding the adder that combines it with the decoded residual, not the other way
/// round. Getting the order backwards — add the residual, then filter — reads the wrong samples through
/// the filter and desyncs from the encoder by an amount that compounds every predicted picture after
/// it, which is exactly the failure a single still frame cannot show.
/// <para/>
/// The filter is switched on or off for all six blocks of a macroblock at once, by its MTYPE (Table
/// 2/H.261): "Inter + MC + FIL" runs it, every other type does not. It is not part of motion
/// compensation and does not require a non-zero vector — the Recommendation says so directly (Table
/// 2's second note), a macroblock may ask for the filter alone by coding "Inter + MC + FIL" with a
/// vector of zero.
/// <para/>
/// It operates independently on each 8x8 block and never reads outside it. The two-dimensional filter
/// is separable into a horizontal pass and a vertical pass, each nominally 1/4, 1/2, 1/4 — but at a
/// block edge, where one of the three taps would have to come from outside the block, that one
/// dimension's filter becomes 0, 1, 0 instead: no reading past the edge and no clamping to it either,
/// just no filtering in that direction for that pel. A corner pel, where both directions are at an
/// edge, is therefore untouched. Full precision is kept between the two passes and only the final
/// two-dimensional result is rounded to an 8-bit sample, with a fractional half rounding up.
/// </remarks>
internal static class H261LoopFilter {

  /// <summary>Filters an 8x8 block of prediction samples in place.</summary>
  internal static void Apply(Span<int> block) {
    Span<int> horizontal = stackalloc int[64];

    // Horizontal pass, scaled by four so no precision is lost before the vertical pass. An edge
    // column keeps its own sample scaled by the same factor, which is what lets both passes share one
    // division at the very end.
    for (var y = 0; y < 8; ++y) {
      var row = y * 8;
      for (var x = 0; x < 8; ++x)
        horizontal[row + x] = x == 0 || x == 7
          ? block[row + x] * 4
          : block[row + x - 1] + 2 * block[row + x] + block[row + x + 1];
    }

    // Vertical pass over the horizontal pass's output, scaled by a further four — sixteen overall,
    // which is why the final division below is by sixteen and rounds a fractional half upward.
    for (var x = 0; x < 8; ++x)
      for (var y = 0; y < 8; ++y) {
        var sum = y == 0 || y == 7
          ? horizontal[y * 8 + x] * 4
          : horizontal[(y - 1) * 8 + x] + 2 * horizontal[y * 8 + x] + horizontal[(y + 1) * 8 + x];

        block[y * 8 + x] = (sum + 8) >> 4;
      }
  }
}
