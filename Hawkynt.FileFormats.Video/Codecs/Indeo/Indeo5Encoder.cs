using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Indeo;

/// <summary>Writes the basic, one-band YVU9 profile of Intel Indeo Video Interactive 5.</summary>
/// <remarks>
/// Every picture is intra coded. Luminance uses 16x16 macroblocks containing four 8x8 slant blocks;
/// each chrominance plane uses one 4x4 slant block for the corresponding macroblock. That pairing is
/// important: YVU9 is subsampled four to one in both dimensions, so the three planes then contain the
/// same number of macroblocks and can use Indeo's inherited macroblock geometry without ambiguity.
/// <para/>
/// The lowest quantiser is used throughout. Indeo still scales some of the higher-frequency 8x8
/// luminance coefficients at that level, so the writer quantises each forward coefficient against the
/// exact inverse-dequantisation formula the decoder applies; the four-by-four chrominance weights at
/// that level are all zero or one. Coefficients are written through the default block codebook and
/// run-value map, using the format's escape for every non-zero coefficient. That is larger than
/// choosing short map entries, but it keeps the entropy layer deliberately simple and deterministic.
/// </remarks>
internal sealed class Indeo5Encoder {

  private const int _PICTURE_START_CODE = 0x1F;
  private const int _PICTURE_SIZE_ESCAPE = 15;
  private const int _END_OF_BLOCK = 4;
  private const int _ESCAPE = 11;
  private const int _MAX_SIGNED_MAGNITUDE = 2048;

  private static readonly IviHuffmanEncoder _BlockCodebook =
    IviHuffmanEncoder.FromDescriptor(IviTables.BlockDescriptors[7]);

  private readonly int _width;
  private readonly int _height;
  private int _frameNumber;

  internal Indeo5Encoder(int width, int height) {
    if (width <= 0 || height <= 0 || width > 0x1FFF || height > 0x1FFF)
      throw new NotSupportedException(
        $"Indeo 5's explicit picture-size fields hold 1 to 8191 samples per dimension; {width}x{height} was supplied.");

    this._width = width;
    this._height = height;
  }

  internal byte[] Encode(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Indeo 5 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var picture = _ToYvu9(frame);
    var writer = new IviBitWriter();

    writer.Write(_PICTURE_START_CODE, 5);
    writer.Write(Indeo5Decoder.FrameTypeIntra, 3);
    writer.Write((uint)this._frameNumber, 8);
    this._frameNumber = (this._frameNumber + 1) & 0xFF;

    this._WriteGroupHeader(writer);
    this._WritePictureHeader(writer);

    // Plane order in the bitstream is Y, Cr, Cb. IviPicture names the chrominance planes by colour
    // difference instead, so the red plane intentionally precedes the blue one here.
    _WriteBand(writer, picture.Luma, picture.Width, picture.Height, blockSize: 8, macroblockSize: 16);
    _WriteBand(writer, picture.ChromaRed, picture.ChromaWidth, picture.ChromaHeight, blockSize: 4, macroblockSize: 4);
    _WriteBand(writer, picture.ChromaBlue, picture.ChromaWidth, picture.ChromaHeight, blockSize: 4, macroblockSize: 4);

    return writer.ToArray();
  }

  private void _WriteGroupHeader(IviBitWriter writer) {
    // No size field, protection, YV12 flag, transparency, explicit tiling or other extension.
    writer.Write(0, 8);
    writer.Write(0, 2); // 0 * 3 + 1 = one luminance band.
    writer.WriteBit(0); // 0 * 3 + 1 = one chrominance band.
    writer.Write(_PICTURE_SIZE_ESCAPE, 4);
    writer.Write((uint)this._height, 13);
    writer.Write((uint)this._width, 13);

    // Luminance: full-pixel motion precision, 8x8 blocks, four blocks per 16x16 macroblock.
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0); // no extended transform information
    writer.Write(0, 2);

    // Chrominance: full-pixel precision, one 4x4 block per macroblock.
    writer.WriteBit(0);
    writer.WriteBit(1);
    writer.WriteBit(1);
    writer.WriteBit(0);
    writer.Write(0, 2);

    writer.Align();
    writer.Write(0, 23); // reserved group-header bits
    writer.WriteBit(0);  // no group extension
    writer.Align();
  }

  private static void _WritePictureHeader(IviBitWriter writer) {
    // No picture-size/checksum/extension fields and the default macroblock Huffman codebook.
    writer.Write(0, 8);
    writer.Write(0, 3);
    writer.Align();
  }

  private static void _WriteBand(
    IviBitWriter writer, ReadOnlySpan<byte> plane, int width, int height, int blockSize, int macroblockSize) {
    // No inheritance, quantiser deltas, corrections, explicit run-value map, custom codebook,
    // checksum or extension. The absent map selector means map eight; the absent codebook means book
    // seven. Quantiser zero is the least-lossy level the format defines.
    writer.Write(0, 8);
    writer.WriteBit(0);
    writer.Write(0, 5);
    writer.Align();

    var body = _WriteTileBody(plane, width, height, blockSize, macroblockSize);
    var tileSize = checked(body.Length + 5);
    if (tileSize > 0xFFFFFF)
      throw new NotSupportedException(
        $"One Indeo 5 tile needs {tileSize} bytes, beyond the format's 24-bit long tile-size field.");

    // A long size keeps the tile prefix fixed at five bytes, so its own encoded length does not alter
    // the value it is trying to state. The decoder includes this prefix in tile.DataSize.
    writer.WriteBit(0); // non-empty tile
    writer.WriteBit(1); // size follows
    writer.Write(0xFF, 8);
    writer.Write((uint)tileSize, 24);
    writer.Align();
    writer.WriteBytes(body);
  }

  private static byte[] _WriteTileBody(
    ReadOnlySpan<byte> plane, int width, int height, int blockSize, int macroblockSize) {
    var writer = new IviBitWriter();
    var macroblockColumns = (width + macroblockSize - 1) / macroblockSize;
    var macroblockRows = (height + macroblockSize - 1) / macroblockSize;
    var blocksPerMacroblock = macroblockSize == blockSize ? 1 : 4;
    var codedPattern = (1u << blocksPerMacroblock) - 1;

    // Macroblock descriptions precede all block data in a tile.
    for (var macroblock = 0; macroblock < macroblockColumns * macroblockRows; ++macroblock) {
      writer.WriteBit(0); // an intra macroblock cannot repeat a reference
      writer.Write(codedPattern, blocksPerMacroblock);
    }
    writer.Align();

    var previousDc = 0;
    Span<int> samples = stackalloc int[64];
    Span<int> coefficients = stackalloc int[64];

    for (var my = 0; my < macroblockRows; ++my)
      for (var mx = 0; mx < macroblockColumns; ++mx)
        for (var block = 0; block < blocksPerMacroblock; ++block) {
          var blockX = mx * macroblockSize + (block & 1) * blockSize;
          var blockY = my * macroblockSize + (block >> 1) * blockSize;
          var count = blockSize * blockSize;

          _LoadBlock(plane, width, height, blockX, blockY, blockSize, samples[..count]);
          if (blockSize == 8)
            IviForwardTransforms.Slant8x8(samples, coefficients);
          else
            IviForwardTransforms.Slant4x4(samples, coefficients);

          if (blockSize == 8)
            _QuantiseLuma(coefficients);

          var dc = coefficients[0];
          coefficients[0] -= previousDc;
          previousDc = dc;
          _WriteCoefficients(writer, coefficients[..count], blockSize == 8 ? IviTables.ZigzagDirect : IviTables.DirectScan4x4);
        }

    writer.Align();
    return writer.ToArray();
  }

  private static void _QuantiseLuma(Span<int> coefficients) {
    var weights = Indeo5Tables.BaseQuant8x8Intra[0];
    var scale = Indeo5Tables.ScaleQuant8x8Intra[0][0];

    for (var i = 0; i < 64; ++i) {
      var weight = weights[i] * scale >> 9;
      if (weight <= 1 || coefficients[i] == 0)
        continue;

      var value = coefficients[i];
      var sign = value < 0 ? -1 : 1;
      var magnitude = Math.Abs(value);
      var offset = ((weight ^ 1) - 1) >> 1;
      var numerator = magnitude - offset;

      if (numerator <= 0) {
        coefficients[i] = 0;
        continue;
      }

      var quantised = (numerator + (weight >> 1)) / weight;
      var reconstructed = quantised * weight + offset;
      if (Math.Abs(magnitude - reconstructed) >= magnitude) {
        coefficients[i] = 0;
        continue;
      }

      coefficients[i] = sign * quantised;
    }
  }

  private static void _LoadBlock(
    ReadOnlySpan<byte> plane, int width, int height, int blockX, int blockY, int blockSize, Span<int> destination) {
    // The decoder keeps padded blocks around the right and bottom edges. Replicating the last real
    // sample into that padding avoids inventing a sharp edge that the transform would ring back into
    // the visible part of a partial block.
    for (var y = 0; y < blockSize; ++y) {
      var sourceY = Math.Min(blockY + y, height - 1);
      for (var x = 0; x < blockSize; ++x) {
        var sourceX = Math.Min(blockX + x, width - 1);
        destination[y * blockSize + x] = plane[sourceY * width + sourceX] - 128;
      }
    }
  }

  private static void _WriteCoefficients(IviBitWriter writer, ReadOnlySpan<int> coefficients, ReadOnlySpan<byte> scan) {
    var previousPosition = -1;

    for (var position = 0; position < coefficients.Length; ++position) {
      var value = coefficients[scan[position]];
      if (value == 0)
        continue;

      var run = position - previousPosition;
      if ((uint)(run - 1) >= 64)
        throw new InvalidDataException($"An Indeo coefficient run of {run} cannot fit the escape syntax.");

      if (value < -(_MAX_SIGNED_MAGNITUDE - 1) || value > _MAX_SIGNED_MAGNITUDE)
        throw new InvalidDataException(
          $"A forward Indeo slant coefficient of {value} cannot fit the escape syntax's twelve signed bits.");

      _BlockCodebook.Write(writer, _ESCAPE);
      _BlockCodebook.Write(writer, run - 1);

      var signed = value > 0 ? (value << 1) - 1 : (-value) << 1;
      _BlockCodebook.Write(writer, signed & 0x3F);
      _BlockCodebook.Write(writer, signed >> 6);
      previousPosition = position;
    }

    _BlockCodebook.Write(writer, _END_OF_BLOCK);
  }

  private static IviPicture _ToYvu9(RawImage frame) {
    var source = frame.Format == PixelFormat.Yuv444P8
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);

    var width = frame.Width;
    var height = frame.Height;
    var chromaWidth = (width + 3) >> 2;
    var chromaHeight = (height + 3) >> 2;
    var samples = width * height;
    var luma = source.GetPlaneData(0)[..samples].ToArray();
    var blue = new byte[chromaWidth * chromaHeight];
    var red = new byte[chromaWidth * chromaHeight];

    _Subsample(source.GetPlaneData(1), blue);
    _Subsample(source.GetPlaneData(2), red);

    return new(width, height, chromaWidth, chromaHeight, luma, blue, red);

    void _Subsample(ReadOnlySpan<byte> from, Span<byte> to) {
      for (var cy = 0; cy < chromaHeight; ++cy) {
        var firstY = cy << 2;
        var lastY = Math.Min(firstY + 4, height);

        for (var cx = 0; cx < chromaWidth; ++cx) {
          var firstX = cx << 2;
          var lastX = Math.Min(firstX + 4, width);
          var sum = 0;
          var count = 0;

          for (var y = firstY; y < lastY; ++y)
            for (var x = firstX; x < lastX; ++x) {
              sum += from[y * width + x];
              ++count;
            }

          to[cy * chromaWidth + cx] = (byte)((sum + (count >> 1)) / count);
        }
      }
    }
  }
}
