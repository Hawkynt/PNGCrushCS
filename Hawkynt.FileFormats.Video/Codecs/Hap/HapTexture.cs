namespace FileFormat.Codecs.Hap;

/// <summary>One decompressed texture out of a Hap frame — a pixel format and the block-compressed
/// bytes in it, still to be run through the block decoder that format names.</summary>
/// <param name="Format">Which block compression the bytes are in.</param>
/// <param name="Data">The decompressed texture bytes: DXT1, DXT5, BC7, RGTC1 or BC6 blocks, in raster
/// order, with no header of their own — the second-stage compressor and the section framing are gone
/// by the time this exists.</param>
internal readonly record struct HapTexture(HapPixelFormat Format, byte[] Data);
