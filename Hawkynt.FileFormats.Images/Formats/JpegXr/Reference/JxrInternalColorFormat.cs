namespace SharpAstro.Jxr;

/// <summary>
/// INTERNAL_CLR_FMT — the codestream's internal colour representation
/// (T.832 §8.3.6 / Table 28). This is independent of the on-disk
/// <see cref="JxrPixelFormat"/>: the encoder converts container pixels
/// (e.g. <c>Rgb24Bpp</c>) into one of these internal formats before applying
/// the transform pipeline.
/// </summary>
/// <remarks>
/// Enum values match the bitstream encoding so they can be written to /
/// read from the codestream header without translation tables.
/// </remarks>
public enum JxrInternalColorFormat
{
    YOnly       = 0,
    YUV420      = 1,
    YUV422      = 2,
    YUV444      = 3,
    YUVK        = 4,
    NComponent  = 6,
    Rgb         = 7,
    Rgbe        = 8,
}
