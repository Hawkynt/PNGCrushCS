using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Writes an AV1 still picture as a lossless 4:4:4 key frame with the identity colour matrix, so
/// the coded planes are the input's own G, B and R samples.
/// </summary>
/// <remarks>
/// This is a conformant AV1 encoder rather than a competitive one. It makes no rate decisions: DC
/// prediction, the largest partition that fits, one transform size and a quantiser step of one.
/// What it buys is a stream any AV1 decoder reads back sample for sample, which is the property an
/// image library actually needs from its writer; smaller output would mean trial-encoding modes,
/// and that is a different piece of work.
/// </remarks>
internal static class Av1StillPictureEncoder {

  private const int _SEQ_PROFILE_444 = 1;

  /// <summary>Level index 31, "maximum parameters", which imposes no size or rate limit.</summary>
  private const int _SEQ_LEVEL_MAX = 31;

  private const int _MAX_TILE_WIDTH_SB = Av1Constants.MaxTileWidth >> 6;
  private const int _MAX_TILE_AREA_SB = Av1Constants.MaxTileArea >> 12;
  private const int _TILE_SIZE_BYTES = 4;

  /// <summary>Encodes an interleaved RGB24 raster as a temporal-delimiter-free AV1 OBU stream.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rgb, int width, int height) {
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "AV1: the picture must have a positive size.");
    if (rgb.Length < width * height * 3)
      throw new ArgumentException("AV1: the RGB raster is shorter than its stated size.", nameof(rgb));

    var miCols = 2 * ((width + 7) >> 3);
    var miRows = 2 * ((height + 7) >> 3);

    // The identity matrix codes G, B and R in the Y, U and V planes, in that order.
    var source = new Av1EncodedPlanes(3, width, height, miCols, miRows, 0, 0);
    source.FillPlane(0, rgb, width * 3, 1, 3);
    source.FillPlane(1, rgb, width * 3, 2, 3);
    source.FillPlane(2, rgb, width * 3, 0, 3);

    var sequence = _BuildSequenceHeader(width, height);
    var frame = _BuildFrameObu(sequence, source, width, height, miCols, miRows);

    var stream = new byte[sequence.Obu.Length + frame.Length];
    sequence.Obu.CopyTo(stream, 0);
    frame.CopyTo(stream, sequence.Obu.Length);
    return stream;
  }

  private sealed record _SequenceHeader(byte[] Obu, Av1SequenceHeader Parsed);

  private static _SequenceHeader _BuildSequenceHeader(int width, int height) {
    var widthBits = _BitsRequired(width - 1);
    var heightBits = _BitsRequired(height - 1);
    var bits = new Av1BitWriter();

    bits.Write(_SEQ_PROFILE_444, 3);
    bits.WriteBit(1); // still_picture
    bits.WriteBit(1); // reduced_still_picture_header
    bits.Write(_SEQ_LEVEL_MAX, 5); // seq_level_idx[0]

    bits.Write((uint)(widthBits - 1), 4);
    bits.Write((uint)(heightBits - 1), 4);
    bits.Write((uint)(width - 1), widthBits);
    bits.Write((uint)(height - 1), heightBits);

    bits.WriteBit(0); // use_128x128_superblock
    bits.WriteBit(0); // enable_filter_intra
    bits.WriteBit(0); // enable_intra_edge_filter
    bits.WriteBit(0); // enable_superres
    bits.WriteBit(0); // enable_cdef
    bits.WriteBit(0); // enable_restoration

    // color_config(). Profile 1 fixes mono_chrome to 0 and does not code it. Declaring BT.709
    // primaries with the sRGB transfer and the identity matrix makes the specification derive full
    // range and 4:4:4 without further bits, which is exactly the lossless RGB case.
    bits.WriteBit(0); // high_bitdepth
    bits.WriteBit(1); // color_description_present_flag
    bits.Write((uint)Av1ColorPrimaries.Bt709, 8);
    bits.Write((uint)Av1TransferCharacteristics.Srgb, 8);
    bits.Write((uint)Av1MatrixCoefficients.Identity, 8);
    bits.WriteBit(0); // separate_uv_delta_q

    bits.WriteBit(0); // film_grain_params_present
    bits.WriteTrailingBits();

    var payload = bits.ToArray();
    var obu = _WrapSizedObu(Av1ObuType.SequenceHeader, payload);
    var parsed = Av1SequenceHeader.Parse(payload, 0, payload.Length);
    return new(obu, parsed);
  }

  private static byte[] _BuildFrameObu(
    _SequenceHeader sequence, Av1EncodedPlanes source, int width, int height, int miCols, int miRows
  ) {
    var sbCols = (miCols + 15) >> 4;
    var sbRows = (miRows + 15) >> 4;
    var minLog2TileCols = _TileLog2(_MAX_TILE_WIDTH_SB, sbCols);
    var maxLog2TileCols = _TileLog2(1, Math.Min(sbCols, Av1Constants.MaxTileCols));
    var maxLog2TileRows = _TileLog2(1, Math.Min(sbRows, Av1Constants.MaxTileRows));
    var minLog2Tiles = Math.Max(minLog2TileCols, _TileLog2(_MAX_TILE_AREA_SB, sbRows * sbCols));

    var tileColsLog2 = minLog2TileCols;
    var tileRowsLog2 = Math.Max(minLog2Tiles - tileColsLog2, 0);

    var tileWidthSb = (sbCols + (1 << tileColsLog2) - 1) >> tileColsLog2;
    var tileHeightSb = (sbRows + (1 << tileRowsLog2) - 1) >> tileRowsLog2;
    var colStarts = _TileStarts(sbCols, tileWidthSb);
    var rowStarts = _TileStarts(sbRows, tileHeightSb);
    var tileCols = colStarts.Length - 1;
    var tileRows = rowStarts.Length - 1;

    var bits = new Av1BitWriter();
    bits.WriteBit(0); // disable_cdf_update: the encoder adapts alongside the decoder
    bits.WriteBit(0); // allow_screen_content_tools, which also removes the palette syntax
    bits.WriteBit(0); // render_and_frame_size_different

    // tile_info()
    bits.WriteBit(1); // uniform_tile_spacing_flag
    for (var log2 = minLog2TileCols; log2 < maxLog2TileCols; ++log2) {
      bits.WriteBit(log2 < tileColsLog2 ? 1 : 0);
      if (log2 >= tileColsLog2)
        break;
    }
    var minLog2TileRows = Math.Max(minLog2Tiles - tileColsLog2, 0);
    for (var log2 = minLog2TileRows; log2 < maxLog2TileRows; ++log2) {
      bits.WriteBit(log2 < tileRowsLog2 ? 1 : 0);
      if (log2 >= tileRowsLog2)
        break;
    }

    if (tileColsLog2 > 0 || tileRowsLog2 > 0) {
      bits.Write(0, tileRowsLog2 + tileColsLog2); // context_update_tile_id
      bits.Write(_TILE_SIZE_BYTES - 1, 2);
    }

    // quantization_params(): a base index of zero with no deltas is what makes the frame lossless,
    // which in turn removes the loop filter, CDEF, restoration and transform-mode syntax below.
    bits.Write(0, 8); // base_q_idx
    bits.WriteBit(0); // delta_q_y_dc coded
    bits.WriteBit(0); // delta_q_u_dc coded
    bits.WriteBit(0); // delta_q_u_ac coded
    bits.WriteBit(0); // using_qmatrix

    bits.WriteBit(0); // segmentation_enabled
    bits.WriteBit(0); // reduced_tx_set
    bits.ByteAlign();

    var header = bits.ToArray();
    var tiles = _EncodeTiles(sequence, source, header, width, height, miCols, miRows, colStarts, rowStarts, tileCols, tileRows);

    var payload = new byte[header.Length + tiles.Length];
    header.CopyTo(payload, 0);
    tiles.CopyTo(payload, header.Length);
    return _WrapSizedObu(Av1ObuType.Frame, payload);
  }

  private static byte[] _EncodeTiles(
    _SequenceHeader sequence, Av1EncodedPlanes source, byte[] header,
    int width, int height, int miCols, int miRows,
    int[] colStarts, int[] rowStarts, int tileCols, int tileRows
  ) {
    // Parsing back the header the encoder just wrote is the cheapest way to guarantee that the tile
    // coder and the decoder agree on the frame parameters: one of them would otherwise be a
    // paraphrase of the other.
    var frameHeader = Av1FrameHeader.Parse(header, 0, header.Length, sequence.Parsed);
    if (!frameHeader.CodedLossless)
      throw new InvalidOperationException("AV1: the encoder's own frame header did not describe a lossless frame.");

    var reconstruction = new Av1DecodedFrame(sequence.Parsed, width, height);
    var frameCdf = Av1CdfContext.CreateDefault(0);
    var encoder = new Av1TileEncoder(source, reconstruction, frameCdf);

    var numTiles = tileCols * tileRows;
    var partitions = new byte[numTiles][];
    for (var tile = 0; tile < numTiles; ++tile)
      partitions[tile] = encoder.EncodeTile(colStarts, rowStarts, tile % tileCols, tile / tileCols, false);

    var output = new List<byte>();
    if (numTiles > 1)
      output.Add(0); // tile_start_and_end_present_flag = 0, then byte_alignment() pads the rest

    for (var tile = 0; tile < numTiles; ++tile) {
      if (tile != numTiles - 1) {
        var size = partitions[tile].Length - 1;
        for (var i = 0; i < _TILE_SIZE_BYTES; ++i)
          output.Add((byte)(size >> (i * 8)));
      }

      output.AddRange(partitions[tile]);
    }

    return output.ToArray();
  }

  private static int[] _TileStarts(int totalSb, int stepSb) {
    var starts = new List<int>();
    for (var start = 0; start < totalSb; start += stepSb)
      starts.Add(start);
    starts.Add(totalSb);
    return starts.ToArray();
  }

  private static byte[] _WrapSizedObu(Av1ObuType type, byte[] payload) {
    var result = new List<byte>(payload.Length + 10) {
      (byte)(((int)type << 3) | 0x02), // obu_has_size_field = 1
    };
    _WriteLeb128(result, (ulong)payload.Length);
    result.AddRange(payload);
    return result.ToArray();
  }

  private static void _WriteLeb128(List<byte> target, ulong value) {
    do {
      var next = (byte)(value & 0x7F);
      value >>= 7;
      if (value != 0)
        next |= 0x80;
      target.Add(next);
    } while (value != 0);
  }

  private static int _TileLog2(int blockSize, int target) {
    var k = 0;
    while ((blockSize << k) < target)
      ++k;
    return k;
  }

  private static int _BitsRequired(int value) {
    var bits = 1;
    while ((value >>= 1) != 0)
      ++bits;
    return bits;
  }
}
