namespace SharpAstro.Jxr;

/// <summary>
/// PROFILE_IDC values from T.832 Annex B / Table B.1 — controls which
/// optional syntax elements a conforming decoder must handle. The encoder
/// emits the highest profile whose features the codestream actually uses.
/// </summary>
public static class JxrProfile
{
    /// <summary>Sub-baseline profile — minimal feature subset.</summary>
    public const byte SubBaseline = 44;

    /// <summary>Baseline profile.</summary>
    public const byte Baseline    = 55;

    /// <summary>Main profile — most common conformance point.</summary>
    public const byte Main        = 66;

    /// <summary>Advanced profile — superset of Main; includes high-bit-depth float and tiling extensions.</summary>
    public const byte Advanced    = 111;
}

/// <summary>
/// LEVEL_IDC values from T.832 Annex B / Table B.2 — caps on picture
/// size, sample rate, and buffer requirements. <see cref="L4"/> covers most
/// typical photo-scale images.
/// </summary>
public static class JxrLevel
{
    public const byte L1 = 4;
    public const byte L2 = 8;
    public const byte L3 = 16;
    public const byte L4 = 32;
    public const byte L5 = 64;
    public const byte L6 = 128;
    public const byte LUnrestricted = 255;
}
