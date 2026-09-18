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
/// colour bits on average (<see cref="ProResProfile.BitsPerMacroblock"/>), which gives the picture a
/// byte budget; the quantisation index is the smallest one whose coded colour fits it, found by
/// bisection over the 1 to 224 that 6.3.1 permits and then confirmed, because coded size falls with
/// the index without being strictly monotone in it. Lossless alpha is deliberately outside that
/// budget: its size is determined by the matte and Apple's published 4444 rates are stated without
/// alpha.
/// <para/>
/// The transform runs once per picture and only the quantisation and the entropy coding are repeated,
/// so the search costs a fraction of the coding it decides.
/// </remarks>
internal static class ProResPictureEncoder {

  /// <summary>The eight bytes of a picture header, RDD 36:2022, 5.2.1.</summary>
  private const int _PICTURE_HEADER_SIZE = 8;

  /// <summary>The six colour-size bytes of a slice header when no alpha is present.</summary>
  private const int _SLICE_HEADER_SIZE = 6;

  /// <summary>The two extra bytes that state the red-difference size when alpha follows it.</summary>
  private const int _ALPHA_SLICE_HEADER_SIZE = 8;

  /// <summary>The largest quantisation index RDD 36:2022, 6.3.1 permits.</summary>
  private const int _MAXIMUM_QUANTISATION_INDEX = 224;

  /// <summary>The largest coded size a slice's own table entry can state.</summary>
  private const int _MAXIMUM_SLICE_SIZE = 0xFFFF;

  /// <summary>One slice's transformed coefficients and optional already-coded lossless alpha.</summary>
  private sealed record Slice(
    double[] Luma,
    double[] Cb,
    double[] Cr,
    byte[] Alpha,
    int LumaBlocks,
    int ChromaBlocks);

  /// <summary>Codes one frame picture or one field picture.</summary>
  /// <param name="planes">The component samples, padded out to whole macroblocks.</param>
  /// <param name="profile">The profile whose weights and colour data rate apply.</param>
  /// <param name="widthInMacroblocks">The picture's width in macroblocks.</param>
  /// <param name="heightInMacroblocks">The picture's coded height in macroblocks.</param>
  /// <param name="pictureHeight">The picture's actual height before bottom padding.</param>
  /// <param name="log2DesiredSliceSize">The picture header's <c>log2_desired_slice_size_in_mb</c>.</param>
  /// <param name="interlaced">Whether this is a field picture, selecting Figure 5's coefficient scan.</param>
  internal static byte[] Encode(
    ProResPlanes planes,
    ProResProfile profile,
    int widthInMacroblocks,
    int heightInMacroblocks,
    int pictureHeight,
    int log2DesiredSliceSize,
    bool interlaced) {
    if (pictureHeight <= 0 || pictureHeight > heightInMacroblocks * 16)
      throw new ArgumentOutOfRangeException(nameof(pictureHeight));

    var sliceSizes = ProResSliceLayout.Build(widthInMacroblocks, log2DesiredSliceSize);
    var sliceCount = (long)heightInMacroblocks * sliceSizes.Length;
    if (sliceCount > ushort.MaxValue)
      throw new InvalidDataException(
        $"A ProRes picture would contain {sliceCount} slices, but its picture header can state at most {ushort.MaxValue}.");

    var slices = _Transform(planes, sliceSizes, heightInMacroblocks, pictureHeight);
    var scan = interlaced ? ProResScan.Interlaced : ProResScan.Progressive;

    var budget = ((long)profile.BitsPerMacroblock * widthInMacroblocks * heightInMacroblocks + 7) / 8;
    var writer = new ProResBitWriter();
    var scanned = _ScratchFor(slices);
    var index = _ChooseQuantisationIndex(slices, profile, budget, writer, scanned, scan);

    var coded = new byte[slices.Length][];
    var total = 0;
    for (var i = 0; i < slices.Length; ++i) {
      coded[i] = _EncodeSlice(slices[i], profile, index, writer, scanned, scan);
      total = checked(total + coded[i].Length);
    }

    return _Assemble(coded, total, log2DesiredSliceSize);
  }

  /// <summary>Lays the coded slices out behind the picture header and its slice table.</summary>
  private static byte[] _Assemble(byte[][] slices, int total, int log2DesiredSliceSize) {
    if (slices.Length > ushort.MaxValue)
      throw new InvalidDataException(
        $"A ProRes picture contains {slices.Length} slices, but its picture header can state at most {ushort.MaxValue}.");

    var pictureSize = checked(_PICTURE_HEADER_SIZE + slices.Length * 2 + total);
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

  /// <summary>The smallest quantisation index whose coded colour picture fits the profile's budget.</summary>
  private static int _ChooseQuantisationIndex(
    Slice[] slices,
    ProResProfile profile,
    long budget,
    ProResBitWriter writer,
    int[] scanned,
    int[] scan) {
    var low = 1;
    var high = _MAXIMUM_QUANTISATION_INDEX;

    while (low < high) {
      var middle = (low + high) / 2;
      if (_PictureSize(slices, profile, middle, writer, scanned, scan) <= budget)
        high = middle;
      else
        low = middle + 1;
    }

    while (low < _MAXIMUM_QUANTISATION_INDEX && _PictureSize(slices, profile, low, writer, scanned, scan) > budget)
      ++low;

    return low;
  }

  /// <summary>
  /// Measures only colour. Alpha has no quantiser and therefore cannot participate in choosing one.
  /// </summary>
  private static long _PictureSize(
    Slice[] slices,
    ProResProfile profile,
    int index,
    ProResBitWriter writer,
    int[] scanned,
    int[] scan) {
    long total = 0;
    foreach (var slice in slices)
      total += _SLICE_HEADER_SIZE
        + _ComponentSize(profile.LumaMatrix, slice.Luma, slice.LumaBlocks, index, writer, scanned, scan)
        + _ComponentSize(profile.ChromaMatrix, slice.Cb, slice.ChromaBlocks, index, writer, scanned, scan)
        + _ComponentSize(profile.ChromaMatrix, slice.Cr, slice.ChromaBlocks, index, writer, scanned, scan);

    return total;
  }

  /// <summary>Codes one slice at the picture's quantisation index, RDD 36:2022, 5.3.1.</summary>
  private static byte[] _EncodeSlice(
    Slice slice,
    ProResProfile profile,
    int index,
    ProResBitWriter writer,
    int[] scanned,
    int[] scan) {
    var luma = _Component(profile.LumaMatrix, slice.Luma, slice.LumaBlocks, index, writer, scanned, scan);
    var cb = _Component(profile.ChromaMatrix, slice.Cb, slice.ChromaBlocks, index, writer, scanned, scan);
    var cr = _Component(profile.ChromaMatrix, slice.Cr, slice.ChromaBlocks, index, writer, scanned, scan);
    var hasAlpha = slice.Alpha.Length != 0;
    var headerSize = hasAlpha ? _ALPHA_SLICE_HEADER_SIZE : _SLICE_HEADER_SIZE;

    var size = checked(headerSize + luma.Length + cb.Length + cr.Length + slice.Alpha.Length);
    if (size > _MAXIMUM_SLICE_SIZE)
      throw new InvalidDataException(
        $"A ProRes slice coded to {size} bytes, which its two-byte table entry cannot state.");

    var bytes = new byte[size];
    bytes[0] = (byte)(headerSize << 3);
    bytes[1] = (byte)index;
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)luma.Length);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)cb.Length);
    if (hasAlpha)
      BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)cr.Length);

    var at = headerSize;
    luma.CopyTo(bytes, at);
    at += luma.Length;
    cb.CopyTo(bytes, at);
    at += cb.Length;
    cr.CopyTo(bytes, at);
    at += cr.Length;
    if (hasAlpha)
      slice.Alpha.CopyTo(bytes, at);

    return bytes;
  }

  private static int _ComponentSize(
    byte[] weights,
    double[] transformed,
    int blockCount,
    int index,
    ProResBitWriter writer,
    int[] scanned,
    int[] scan) {
    _Quantise(weights, transformed, blockCount, index, scanned, scan);
    writer.Reset();
    ProResCoefficients.Encode(writer, scanned.AsSpan(0, blockCount * 64), blockCount);

    return (writer.BitCount + 7) / 8;
  }

  private static byte[] _Component(
    byte[] weights,
    double[] transformed,
    int blockCount,
    int index,
    ProResBitWriter writer,
    int[] scanned,
    int[] scan) {
    _Quantise(weights, transformed, blockCount, index, scanned, scan);
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

  /// <summary>Quantises transformed blocks straight into the scan order they are coded in.</summary>
  private static void _Quantise(
    byte[] weights,
    double[] transformed,
    int blockCount,
    int index,
    int[] scanned,
    int[] scan) {
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
  private static Slice[] _Transform(
    ProResPlanes planes,
    int[] sliceSizes,
    int heightInMacroblocks,
    int pictureHeight) {
    var chromaBlocks = planes.ChromaWidth == planes.Width ? 4 : 2;
    var slices = new Slice[checked(heightInMacroblocks * sliceSizes.Length)];
    var at = 0;

    for (var row = 0; row < heightInMacroblocks; ++row) {
      var macroblock = 0;
      var sliceHeight = Math.Min(16, pictureHeight - row * 16);
      if (sliceHeight <= 0)
        throw new InvalidDataException("The coded ProRes macroblock rows exceed the picture height they describe.");

      for (var j = 0; j < sliceSizes.Length; ++j) {
        var sliceMacroblocks = sliceSizes[j];
        var alpha = planes.Alpha is null
          ? []
          : ProResAlpha.Encode(
            planes.Alpha,
            planes.AlphaBitDepth,
            planes.Width,
            macroblock * 16,
            row * 16,
            sliceMacroblocks * 16,
            sliceHeight);

        slices[at++] = new(
          _TransformComponent(planes, 0, 4, sliceMacroblocks, macroblock, row),
          _TransformComponent(planes, 1, chromaBlocks, sliceMacroblocks, macroblock, row),
          _TransformComponent(planes, 2, chromaBlocks, sliceMacroblocks, macroblock, row),
          alpha,
          4 * sliceMacroblocks,
          chromaBlocks * sliceMacroblocks);

        macroblock += sliceMacroblocks;
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
