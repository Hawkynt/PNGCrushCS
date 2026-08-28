namespace SharpAstro.Jxr;

/// <summary>Coefficient band tag (jxrlib <c>BAND</c>, common.h).</summary>
internal enum Band { Header = 0, Dc = 1, Lp = 2, Ac = 3, Fl = 4 }

/// <summary>
/// The per-macroblock adaptive "model bits" — the fixed-length-code (FLC) width
/// used to carry the low bits of significant coefficients, adapted each MB from a
/// Laplacian-mean count of activity. jxrlib <c>CAdaptiveModel</c> (common.h) +
/// <c>UpdateModelMB</c> (image.c). One width per channel group (luma, chroma).
/// </summary>
internal sealed class AdaptiveModel
{
    public readonly int[] FlcState = new int[2];
    public readonly int[] FlcBits = new int[2];
    public readonly Band Band;

    /// <summary>Reset to the jxrlib init: DC width 8, LP width 4, AC width 0; state 0.</summary>
    public AdaptiveModel(Band band)
    {
        Band = band;
        int init = band switch { Band.Dc => 8, Band.Lp => 4, _ => 0 };
        FlcBits[0] = FlcBits[1] = init;
    }
}

/// <summary>
/// <c>UpdateModelMB</c> — adapt the FLC widths at the end of a macroblock. Ported
/// faithfully from jxrlib (image/sys/image.c). The incoming Laplacian means are
/// weighted per band/format, then each channel group's width drifts up or down
/// through a hysteresis state when activity is persistently high or low.
/// </summary>
internal static class ModelBits
{
    private const int ModelWeight = 70;

    private static readonly int[] Weight0 = { 240, 12, 1 }; // DC, LP, AC (luma)
    private static readonly int[][] Weight1 =
    {
        new[] { 0, 240, 120, 80, 60, 48, 40, 34, 30, 27, 24, 22, 20, 18, 17, 16 }, // DC
        new[] { 0, 12, 6, 4, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1 },                  // LP
        new[] { 0, 16, 8, 5, 4, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1 },                  // AC
    };
    private static readonly int[] Weight2 = { 120, 37, 2, /*420*/ 120, 18, 1 /*422*/ };

    /// <summary>
    /// Adapt <paramref name="model"/> from the per-channel Laplacian means
    /// (<paramref name="lapMean"/>, length 2 — luma, chroma). The array is weighted
    /// in place as in jxrlib. <paramref name="channels"/> is the channel count.
    /// </summary>
    public static void UpdateMb(ColorFormat cf, int channels, int[] lapMean, AdaptiveModel model)
    {
        int b = (int)model.Band - (int)Band.Dc; // DC->0, LP->1, AC->2

        lapMean[0] *= Weight0[b];
        if (cf == ColorFormat.Yuv420)
            lapMean[1] *= Weight2[b];
        else if (cf == ColorFormat.Yuv422)
            lapMean[1] *= Weight2[3 + b];
        else
        {
            lapMean[1] *= Weight1[b][channels - 1];
            if (model.Band == Band.Ac)
                lapMean[1] >>= 4;
        }

        for (var j = 0; j < 2; j++)
        {
            int iLM = lapMean[j];
            int iMS = model.FlcState[j];
            int iDelta = (iLM - ModelWeight) >> 2;

            if (iDelta <= -8)
            {
                iDelta += 4;
                if (iDelta < -16) iDelta = -16;
                iMS += iDelta;
                if (iMS < -8)
                {
                    if (model.FlcBits[j] == 0) iMS = -8;
                    else { iMS = 0; model.FlcBits[j]--; }
                }
            }
            else if (iDelta >= 8)
            {
                iDelta -= 4;
                if (iDelta > 15) iDelta = 15;
                iMS += iDelta;
                if (iMS > 8)
                {
                    if (model.FlcBits[j] >= 15) { model.FlcBits[j] = 15; iMS = 8; }
                    else { iMS = 0; model.FlcBits[j]++; }
                }
            }

            model.FlcState[j] = iMS;
            if (cf == ColorFormat.YOnly) break;
        }
    }
}
