namespace SharpAstro.Jxr;

/// <summary>
/// BANDS_PRESENT from the IMAGE_PLANE_HEADER — T.832 §8.4.4 / Table 30.
/// Selects which subbands appear in the codestream; encoders drop the
/// higher-frequency bands to trade fidelity for size.
/// </summary>
public enum JxrBandsPresent
{
    /// <summary>All bands present: DC, LP, HP, and FlexBits refinement.</summary>
    AllBands    = 0,
    /// <summary>DC, LP, HP — but FlexBits refinement layer is omitted.</summary>
    NoFlexbits  = 1,
    /// <summary>DC and LP only — HP and FlexBits omitted.</summary>
    NoHighpass  = 2,
    /// <summary>DC band only — coarsest representation.</summary>
    DcOnly      = 3,
    // 4..15 reserved
}
