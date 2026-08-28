namespace SharpAstro.Jxr;

/// <summary>
/// OUTPUT_BITDEPTH from the IMAGE_HEADER — T.832 §8.3.20 / Table 23.
/// Specifies the bit depth and numerical representation of decoded samples.
/// </summary>
public enum JxrOutputBitDepth
{
    Bd1White1 = 0,
    Bd8       = 1,
    Bd16      = 2,
    Bd16S     = 3,
    Bd16F     = 4,
    // 5 reserved
    Bd32S     = 6,
    Bd32F     = 7,
    Bd5       = 8,
    Bd10      = 9,
    Bd565     = 10,
    // 11-14 reserved
    Bd1Black1 = 15,
}
