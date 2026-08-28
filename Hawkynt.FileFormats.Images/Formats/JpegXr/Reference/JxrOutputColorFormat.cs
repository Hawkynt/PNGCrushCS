namespace SharpAstro.Jxr;

/// <summary>
/// OUTPUT_CLR_FMT from the IMAGE_HEADER — T.832 §8.3.19 / Table 22.
/// Specifies the colour format of decoded output samples (distinct from
/// <see cref="JxrInternalColorFormat"/>, which is the internal codec
/// representation).
/// </summary>
public enum JxrOutputColorFormat
{
    YOnly      = 0,
    YUV420     = 1,
    YUV422     = 2,
    YUV444     = 3,
    Cmyk       = 4,
    // 5 reserved (CMYK_DIRECT in some drafts; jxrlib leaves it commented out)
    NComponent = 6,
    Rgb        = 7,
    Rgbe       = 8,
    // 9..15 reserved
}
