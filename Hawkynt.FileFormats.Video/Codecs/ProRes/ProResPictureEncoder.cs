using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Codes one whole picture: its slices, the table that gives their sizes, and the header over both.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5.2 and 5.3, written to be read alongside <see cref="ProResPictureDecoder"/> — every
/// field this writes is one that one reads, and the two are laid out in the same order for that
/// reason.
/// <para/>
/// <b>Every slice is coded on its own.</b> There is no prediction of any kind across a slice
/// boundary, so the entropy codebooks are restarted for every colour component of every slice and a
/// slice can be coded again at a different quantisation index without disturbing anything around it.
/// That independence is what the format is arranged for, and it is what makes the rate decision below
/// affordable.
/// <para/>
/// <b>The rate decision is one index for the whole picture.</b> A profile allows a macroblock so many
/// bits on average (<see cref="ProResProfile.BitsPerMacroblock"/>), which gives the picture a byte
/// budget; the quantisation index is the smallest one whose coded slices together fit it, found by
/// bisection over the 1 to 224 that 6.3.1 permits and then confirmed, because coded size falls with
/// the index without being strictly monotone in it.
/// <para/>
/// One index rather than one per slice, because the alternative is worse in both directions. Capping
/// each slice at its own share of the budget leaves the easy slices' unspent share unspent and
/// quantises the hard ones far past what the picture as a whole could afford — measured, that coded a
/// detailed 1280x718 picture to under a third of its allowance while pushing the busiest slices to a
/// quantisation index above a hundred. Spending the budget where the picture needs it is what a
/// picture-wide index does for nothing.
/// <para/>
/// The transform runs once per picture and only the quantisation and the entropy coding are repeated,
/// so the search costs a fraction of the coding it decides.
/// </remarks>
internal static class ProResPictureEncoder {

  /// <summary>The eight bytes of a picture header, RDD 36:2022, 5.2.1.</summary>
  private const int _PICTURE_HEADER_SIZE = 8;

  /// <summary>The slice header this writes, RDD 36:2022, 5.3.1, there being no alpha to size.</summary>
  private const int _SLICE_HEADER_SIZE = 6;

  /// <summary>The largest quantisation index RDD 36:2022, 6.3.1 permits.</summary>
  private const int _MAXIMUM_QUANTISATION_INDEX = 224;

  /// <summary>The largest coded size a slice's own table entry can state.</summary>
  private const int _MAXIMUM_SLICE_SIZE = 0xFFFF;

  /// <summary>One slice's transformed coefficients, before any quantisation has been decided.</summary>
  private sealed record Slice(double[] Luma, double[] Cb, double[] Cr, int LumaBlocks, int ChromaBlocks);

  /// <summary>
  /// Codes one picture.
  /// </summary>
  /// <param name="planes">The component samples, padded out to whole macroblocks.</param>
  /// <param name="profile">The profile whose weights and data rate apply.</param>
  /// <param name="widthInMacroblocks">The picture's width in macroblocks.</param>
  /// <param name="heightInMacroblocks">The picture's height in macroblocks.</param>
  /// <param name="log2DesiredSliceSize">The picture header's <c>log2_desired_slice_size_in_mb</c>.</param>
  internal static byte[] Encode(
    ProResPlanes planes,
    ProResProfile profile,
    int widthInMacroblocks,
    int heightInMacroblocks,
    int log2DesiredSliceSize) {
    var sliceSizes = ProResSliceLayout.Build(widthInMacroblocks, log2DesiredSliceSize);
    var slices = _Transform(planes, sliceSizes, heightInMacroblocks);

    var budget = (profile.BitsPerMacroblock * widthInMacroblocks * heightInMacroblocks + 7) / 8;
    var writer = new ProResBitWriter();
    var scanned = _ScratchFor(slices);
    var index = _ChooseQuantisationIndex(slices, profile, budget, writer, scanned);

    var coded = new byte[slices.Length][];
    var total = 0;
    for (var i = 0; i < slices.Length; ++i) {
      coded[i] = _EncodeSlice(slices[i], profile, index, writer, scanned);
      total += coded[i].Length;
    }

    return _Assemble(coded, total, log2DesiredSliceSize);
  }

  /// <summary>Lays the coded slices out behind the picture header and its slice table.</summary>
  private static byte[] _Assemble(byte[][] slices, int total, int log2DesiredSliceSize) {
    var pictureSize = _PICTURE_HEADER_SIZE + slices.Length * 2 + total;
    var picture = new byte[pictureSize];

    // 5.2.1. The header is the eight bytes of its own fixed fields, so the size field holds eight and
    // the slice table starts immediately after it.
    picture[0] = _PICTURE_HEADER_SIZE << 3;
    BinaryPrimitives.WriteUInt32BigEndian(picture.AsSpan(1), (uint)pictureSize);
    BinaryPrimitives.WriteUInt16BigEndian(picture.AsSpan(5), (ushort)slices.Length);
    picture[7] = (byte)(log2DesiredSliceSize << 4);

    var at = _PICTURE_HEADER_SIZE + slices.Length * 2;
    for (var i = 0; i < slices.Length; ++i) {
      BinaryPrimitives.WriteUInt16BigEndian(picture.AsSpan(_PICTURE_HEADER_SIZE + i * 2), (ushort)slices[i].Length);
      slices[i].CopyTo(picture, at);
      at += slices[i].Length;
    }

    return picture;
  }

  /// <summary>
  /// The smallest quantisation index whose coded picture fits the profile's budget.
  /// </summary>
  /// <remarks>
  /// Bisection first, on the assumption that a coarser quantiser codes to fewer bytes; then the
  /// answer is confirmed and raised one step at a time where it was not, because that assumption
  /// holds overwhelmingly but not universally — a coarser quantiser can lengthen a run of zeroes past
  /// the point where its codebook adapts and cost a bit more than the finer one did.
  /// </remarks>
  private static int _ChooseQuantisationIndex(
    Slice[] slices, ProResProfile profile, int budget, ProResBitWriter writer, int[] scanned) {
    var low = 1;
    var high = _MAXIMUM_QUANTISATION_INDEX;

    while (low < high) {
      var middle = (low + high) / 2;
      if (_PictureSize(slices, profile, middle, writer, scanned) <= budget)
        high = middle;
      else
        low = middle + 1;
    }

    while (low < _MAXIMUM_QUANTISATION_INDEX && _PictureSize(slices, profile, low, writer, scanned) > budget)
      ++low;

    return low;
  }

  private static int _PictureSize(
    Slice[] slices, ProResProfile profile, int index, ProResBitWriter writer, int[] scanned) {
    var total = 0;
    foreach (var slice in slices)
      total += _SLICE_HEADER_SIZE
        + _ComponentSize(profile.LumaMatrix, slice.Luma, slice.LumaBlocks, index, writer, scanned)
        + _ComponentSize(profile.ChromaMatrix, slice.Cb, slice.ChromaBlocks, index, writer, scanned)
        + _ComponentSize(profile.ChromaMatrix, slice.Cr, slice.ChromaBlocks, index, writer, scanned);

    return total;
  }

  /// <summary>Codes one slice at the picture's quantisation index, RDD 36:2022, 5.3.1.</summary>
  private static byte[] _EncodeSlice(
    Slice slice, ProResProfile profile, int index, ProResBitWriter writer, int[] scanned) {
    var luma = _Component(profile.LumaMatrix, slice.Luma, slice.LumaBlocks, index, writer, scanned);
    var cb = _Component(profile.ChromaMatrix, slice.Cb, slice.ChromaBlocks, index, writer, scanned);
    var cr = _Component(profile.ChromaMatrix, slice.Cr, slice.ChromaBlocks, index, writer, scanned);

    var size = _SLICE_HEADER_SIZE + luma.Length + cb.Length + cr.Length;
    if (size > _MAXIMUM_SLICE_SIZE)
      throw new InvalidDataException(
        $"A ProRes slice coded to {size} bytes, which its two-byte table entry cannot state.");

    var bytes = new byte[size];
    bytes[0] = _SLICE_HEADER_SIZE << 3;
    bytes[1] = (byte)index;
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)luma.Length);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)cb.Length);
    luma.CopyTo(bytes, _SLICE_HEADER_SIZE);
    cb.CopyTo(bytes, _SLICE_HEADER_SIZE + luma.Length);
    cr.CopyTo(bytes, _SLICE_HEADER_SIZE + luma.Length + cb.Length);

    return bytes;
  }

  private static int _ComponentSize(
    byte[] weights, double[] transformed, int blockCount, int index, ProResBitWriter writer, int[] scanned) {
    _Quantise(weights, transformed, blockCount, index, scanned);
    writer.Reset();
    ProResCoefficients.Encode(writer, scanned.AsSpan(0, blockCount * 64), blockCount);

    return (writer.BitCount + 7) / 8;
  }

  private static byte[] _Component(
    byte[] weights, double[] transformed, int blockCount, int index, ProResBitWriter writer, int[] scanned) {
    _Quantise(weights, transformed, blockCount, index, scanned);
    writer.Reset();
    ProResCoefficients.Encode(writer, scanned.AsSpan(0, blockCount * 64), blockCount);

    return writer.ToArray();
  }

  /// <summary>A buffer large enough for the scanned coefficients of any one component of any slice.</summary>
  private static int[] _ScratchFor(Slice[] slices) {
    var largest = 0;
    foreach (var slice in slices)
      largest = Math.Max(largest, Math.Max(slice.LumaBlocks, slice.ChromaBlocks));

    return new int[largest * 64];
  }

  /// <summary>
  /// Quantises a component's transformed blocks straight into the scanned order they are coded in.
  /// </summary>
  /// <remarks>
  /// The inverse of the one pass <see cref="ProResBlocks.Reconstruct"/> does: 7.3 gives
  /// <c>F[v][u] = QF[v][u] · W[v][u] · qScale / 8</c>, so what goes into the bitstream is
  /// <c>F · 8 / (W · qScale)</c> to nearest, and 7.2.1's index calculation decides where. Doing the
  /// scan here rather than afterwards means the coefficients are already in the order the run-length
  /// coding wants them, which is by frequency across the whole slice and not block by block.
  /// <para/>
  /// The scan is the progressive one: this encoder writes <c>interlace_mode</c> 0, and 7.2.2 makes
  /// that the choice of scan.
  /// </remarks>
  private static void _Quantise(byte[] weights, double[] transformed, int blockCount, int index, int[] scanned) {
    var scan = ProResScan.Progressive;
    var scale = ProResPictureDecoder.QuantisationScale(index);

    Array.Clear(scanned, 0, blockCount * 64);

    for (var block = 0; block < blockCount; ++block) {
      var from = block * 64;

      for (var i = 0; i < 64; ++i) {
        var coefficient = transformed[from + i];
        if (coefficient == 0d)
          continue;

        var quantised = (int)Math.Round(coefficient * 8d / (weights[i] * scale), MidpointRounding.AwayFromZero);
        if (quantised != 0)
          scanned[blockCount * scan[i] + block] = quantised;
      }
    }
  }

  /// <summary>Transforms every block of every slice of the picture, in the order they are coded in.</summary>
  private static Slice[] _Transform(ProResPlanes planes, int[] sliceSizes, int heightInMacroblocks) {
    var chromaBlocks = planes.ChromaWidth == planes.Width ? 4 : 2;
    var slices = new Slice[heightInMacroblocks * sliceSizes.Length];
    var at = 0;

    for (var row = 0; row < heightInMacroblocks; ++row) {
      var macroblock = 0;

      for (var j = 0; j < sliceSizes.Length; ++j) {
        slices[at++] = new(
          _TransformComponent(planes, 0, 4, sliceSizes[j], macroblock, row),
          _TransformComponent(planes, 1, chromaBlocks, sliceSizes[j], macroblock, row),
          _TransformComponent(planes, 2, chromaBlocks, sliceSizes[j], macroblock, row),
          4 * sliceSizes[j],
          chromaBlocks * sliceSizes[j]);

        macroblock += sliceSizes[j];
      }
    }

    return slices;
  }

  /// <summary>
  /// Transforms every block of one colour component of one slice, in the order they are coded in.
  /// </summary>
  /// <remarks>
  /// The blocks run macroblock by macroblock across the slice, and within a macroblock in the order
  /// RDD 36:2022 Figures 6 to 8 lay them out — which is not the same order for the three cases, as
  /// <see cref="ProResPictureDecoder"/> says at more length. The samples are turned back into the
  /// transform's own range on the way in: 7.5.1 makes a sample <c>round(2^b · (v + 256) / 512)</c>,
  /// so <c>v</c> is the sample scaled by <c>512 / 2^b</c> with the mid-range bias taken off.
  /// </remarks>
  private static double[] _TransformComponent(
    ProResPlanes planes,
    int component,
    int blocksPerMacroblock,
    int sliceSizeInMacroblocks,
    int macroblockOffset,
    int macroblockRow) {
    var blocks = new double[blocksPerMacroblock * sliceSizeInMacroblocks * 64];

    var plane = planes.Plane(component);
    var planeWidth = planes.PlaneWidth(component);
    var macroblockWidth = component == 0 || planes.ChromaWidth == planes.Width ? 16 : 8;
    var scale = 512d / (1 << planes.BitDepth);

    for (var m = 0; m < sliceSizeInMacroblocks; ++m) {
      var originX = (macroblockOffset + m) * macroblockWidth;

      for (var b = 0; b < blocksPerMacroblock; ++b) {
        var (blockX, blockY) = ProResPictureDecoder.BlockPosition(component, blocksPerMacroblock, b);
        var into = blocks.AsSpan((blocksPerMacroblock * m + b) * 64, 64);

        for (var y = 0; y < 8; ++y) {
          var row = (macroblockRow * 16 + blockY + y) * planeWidth + originX + blockX;
          for (var x = 0; x < 8; ++x)
            into[y * 8 + x] = plane[row + x] * scale - 256d;
        }

        ProResForwardDct.Transform(into);
      }
    }

    return blocks;
  }
}
