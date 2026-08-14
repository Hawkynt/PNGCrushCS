using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>
/// Symbian's SEpocBitmapHeader, which prefixes every bitmap in an MBM file.
/// </summary>
/// <remarks>
/// The field order is the one the operating system serialises, and the pair of sizes is the part
/// worth naming: the size in pixels is followed by a size in twips, and only then by the depth. A
/// header laid out without the twips reads the depth off the width in twips, which is zero on every
/// file the converters write, so the picture comes out with no depth at all.
/// </remarks>
/// <param name="BitmapSize">Whole bitmap in bytes: this header plus the pixel data.</param>
/// <param name="HeaderLength">Length of this header, 40 as first published and 44 on later releases.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="WidthInTwips">Width in twips, a twentieth of a point. Zero when nothing recorded a physical size.</param>
/// <param name="HeightInTwips">Height in twips.</param>
/// <param name="BitsPerPixel">Bits per pixel: 1, 2, 4, 8, 12, 16, 24 or 32.</param>
/// <param name="ColorMode">Symbian's iColor: 0 greyscale, 1 colour, 2 and 3 colour with alpha at 32 bits.</param>
/// <param name="PaletteSize">Number of palette entries stored with the bitmap, normally 0.</param>
/// <param name="Compression">0 uncompressed, otherwise one of Symbian's RLE packings.</param>
[GenerateSerializer]
public readonly partial record struct SymbianMbmBitmapHeader(
  int BitmapSize,
  int HeaderLength,
  int Width,
  int Height,
  int WidthInTwips,
  int HeightInTwips,
  int BitsPerPixel,
  uint ColorMode,
  uint PaletteSize,
  uint Compression
) {

 public const int StructSize = 40;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SymbianMbmBitmapHeader>();
}
