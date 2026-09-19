namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// The reconstructed component samples of one frame, at the depth the bitstream states.
/// </summary>
/// <remarks>
/// Planes rather than packed pixels, and at the coded depth rather than at eight bits, because that
/// is what the decoding process of SMPTE ST 2019-1:2016, 8 produces and it is what a comparison
/// against another decoder has to be made on. Packing and reduction are display steps that happen
/// once, at the end, in <see cref="DnxHdColorConversion"/>.
/// <para/>
/// The planes are a whole number of macroblocks in both directions. 6.3 has an encoder pad a raster
/// whose height is not a multiple of sixteen with augmentation lines, and a raster whose width is
/// not — for the resolution-independent profile — with augmentation samples, and has a decoder
/// discard both. Both are discarded here at the point the frame is packed, so that a macroblock
/// straddling an edge is still reconstructed whole.
/// <para/>
/// CID 1256 can switch representation macroblock by macroblock when the frame uses the RGB format
/// rules. In that case the three arrays hold the first, second and third coded components and
/// <see cref="DirectRgbMacroblocks"/> records which macroblocks are already R, G and B; the others
/// are Y′, Cb and Cr and are transformed only while the final RGB image is packed.
/// </remarks>
internal sealed class DnxHdPlanes {

  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal required int ChromaWidth { get; init; }
  internal required int ChromaHeight { get; init; }
  internal required int BitDepth { get; init; }
  internal required int MacroblocksPerRow { get; init; }
  internal required bool RgbFormat { get; init; }

  internal required ushort[] Luma { get; init; }
  internal required ushort[] Cb { get; init; }
  internal required ushort[] Cr { get; init; }
  internal bool[]? DirectRgbMacroblocks { get; init; }

  internal static DnxHdPlanes Allocate(int width, int height, int chromaShift, int bitDepth, bool rgbFormat = false) {
    var chromaWidth = width >> chromaShift;
    var macroblocksPerRow = (width + 15) / 16;
    var macroblockRows = (height + 15) / 16;

    return new() {
      Width = width,
      Height = height,
      ChromaWidth = chromaWidth,
      ChromaHeight = height,
      BitDepth = bitDepth,
      MacroblocksPerRow = macroblocksPerRow,
      RgbFormat = rgbFormat,
      Luma = new ushort[width * height],
      Cb = new ushort[chromaWidth * height],
      Cr = new ushort[chromaWidth * height],
      DirectRgbMacroblocks = rgbFormat ? new bool[macroblocksPerRow * macroblockRows] : null,
    };
  }

  /// <summary>The plane a component index names: first, second or third coded component.</summary>
  internal ushort[] Plane(int component) => component switch {
    0 => this.Luma,
    1 => this.Cb,
    _ => this.Cr,
  };

  /// <summary>The width in samples of the plane a component index names.</summary>
  internal int PlaneWidth(int component) => component == 0 ? this.Width : this.ChromaWidth;

  internal void SetDirectRgbMacroblock(int x, int y, bool directRgb) {
    if (this.DirectRgbMacroblocks is null)
      return;

    this.DirectRgbMacroblocks[y * this.MacroblocksPerRow + x] = directRgb;
  }

  internal bool IsDirectRgb(int x, int y)
    => this.DirectRgbMacroblocks?[((y >> 4) * this.MacroblocksPerRow) + (x >> 4)] == true;
}
