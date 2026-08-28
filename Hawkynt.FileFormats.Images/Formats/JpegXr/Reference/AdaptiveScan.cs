namespace SharpAstro.Jxr;

/// <summary>
/// The JPEG XR adaptive coefficient scan (jxrlib <c>CAdaptiveScan</c>). Each of
/// the 16 slots pairs a running <see cref="Total"/> with the physical
/// coefficient index <see cref="Scan"/>. As coefficients at a slot turn out
/// nonzero, that slot's total is bumped and bubbled toward the front, so the
/// scan order tracks the statistics of the band. Ported from the encode
/// (<c>segenc.c AdaptiveScan</c>) / decode (<c>segdec.c</c>) inner loops.
/// </summary>
internal sealed class AdaptiveScan
{
    // common.h MAXTOTAL — pins slot 0 at the front so it never bubbles.
    public const int MaxTotal = 32767;

    /// <summary>Running nonzero counts (slot 0 pinned at <see cref="MaxTotal"/>).</summary>
    public readonly int[] Total = new int[16];

    /// <summary>Physical coefficient index visited at each slot, in scan order.</summary>
    public readonly int[] Scan = new int[16];

    /// <param name="initialScan">Initial scan order (e.g. the zigzag for the band); 16 entries.</param>
    public AdaptiveScan(ReadOnlySpan<int> initialScan) => InitZigzag(initialScan);

    /// <summary>
    /// jxrlib <c>InitZigzagScan</c> + the <c>m_bResetRGITotals</c> ramp: restore the
    /// scan permutation to <paramref name="order"/> and reset the totals. Applied at
    /// each context reset (tile boundary) so a reused context starts clean.
    /// </summary>
    public void InitZigzag(ReadOnlySpan<int> order)
    {
        order.Slice(0, 16).CopyTo(Scan);
        Reset();
    }

    /// <summary>
    /// Reset the totals to the fixed ramp jxrlib applies every 16 MBs
    /// (<c>m_bResetRGITotals</c>): slot 0 = <see cref="MaxTotal"/>, then
    /// 32, 30, 28, … , 4 (iScale = 2, iWeight = 32). The scan order itself is
    /// not reset — only the totals.
    /// </summary>
    public void Reset()
    {
        const int iScale = 2;
        int iWeight = iScale * 16; // 32
        Total[0] = MaxTotal;
        for (var k = 1; k < 16; k++)
        {
            Total[k] = iWeight;
            iWeight -= iScale;
        }
    }

    /// <summary>
    /// Record that the coefficient at scan slot <paramref name="k"/> was significant:
    /// bump its total and, if it now exceeds the previous slot, swap the two slots
    /// (a single adjacent bubble — never a full sort; slot 0 is pinned).
    /// </summary>
    public void Visit(int k)
    {
        Total[k]++;
        if (k > 0 && Total[k] > Total[k - 1])
        {
            (Total[k], Total[k - 1]) = (Total[k - 1], Total[k]);
            (Scan[k], Scan[k - 1]) = (Scan[k - 1], Scan[k]);
        }
    }
}
