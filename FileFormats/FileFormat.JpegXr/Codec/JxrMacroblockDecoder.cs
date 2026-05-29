using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Macroblock-level decoder for JPEG XR (ITU-T T.832).
/// Extracts DC, LP, and HP coefficients from the compressed bitstream,
/// applies dequantization, inverse transform, overlap filtering, and color conversion.
/// </summary>
/// <remarks>
/// A JPEG XR macroblock is 16x16 pixels, composed of 4x4 blocks of 4x4 samples.
/// After the two-stage transform (block PCT + DC sub-band PCT), the coefficients
/// are organized hierarchically:
///
/// - DC band: 1 coefficient per macroblock per channel (the [0,0] of the DC sub-band PCT)
/// - LP band: 15 coefficients per macroblock per channel (positions [0,1]..[3,3] of DC sub-band)
/// - HP band: 15 coefficients per block x 16 blocks = 240 per macroblock per channel
///
/// DPCM prediction is applied to DC values (horizontal from left neighbor).
/// LP prediction can use left and/or top neighbors depending on orientation.
/// HP coefficients are coded directly with run-length VLC.
/// </remarks>
internal sealed class JxrMacroblockDecoder {

  private const int _MB_SIZE = 16;
  private const int _BLOCK_SIZE = 4;
  private const int _BLOCKS_PER_MB = _MB_SIZE / _BLOCK_SIZE;
  private const int _MB_PIXEL_COUNT = _MB_SIZE * _MB_SIZE; // 256
  private const int _DC_SUBBAND_SIZE = _BLOCKS_PER_MB * _BLOCKS_PER_MB; // 16

  private readonly int _channelCount;
  private readonly JxrQuantizer _quantizer;
  private readonly JxrAdaptiveVlcEngine _vlcEngine;

  /// <summary>DC prediction state per channel (DPCM from left macroblock).</summary>
  private readonly int[] _dcPred;

  /// <summary>LP prediction state per channel (15 LP values from left macroblock).</summary>
  private readonly int[][] _lpPredLeft;

  public JxrMacroblockDecoder(int channelCount, JxrQuantizer quantizer, JxrAdaptiveVlcEngine vlcEngine) {
    _channelCount = Math.Max(channelCount, 1);
    _quantizer = quantizer;
    _vlcEngine = vlcEngine;
    _dcPred = new int[_channelCount];
    _lpPredLeft = new int[_channelCount][];
    for (var c = 0; c < _channelCount; ++c)
      _lpPredLeft[c] = new int[15];
  }

  /// <summary>Resets the prediction state (call at the start of each macroblock row).</summary>
  public void ResetRowState() {
    Array.Clear(_dcPred);
    for (var c = 0; c < _channelCount; ++c)
      Array.Clear(_lpPredLeft[c]);
    _vlcEngine.ResetHp();
  }

  /// <summary>
  /// Decodes one macroblock for all channels from the bitstream.
  /// Returns the channel buffers (each 256 elements) in spatial domain, ready for pixel output.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the macroblock data.</param>
  /// <param name="channels">Pre-allocated channel buffers, one per channel, each with 256 elements.</param>
  /// <param name="bands">Which frequency bands are present in the bitstream.</param>
  public void DecodeMacroblock(JxrBitReader reader, int[][] channels, JxrBandsPresent bands) {
    for (var c = 0; c < _channelCount; ++c) {
      Array.Clear(channels[c]);
      _DecodeChannelMacroblock(reader, channels[c], c, bands);
    }
  }

  /// <summary>
  /// Decodes one macroblock for a single channel.
  /// Steps: decode DC/LP/HP -> dequantize -> arrange in macroblock -> inverse LBT.
  /// </summary>
  private void _DecodeChannelMacroblock(JxrBitReader reader, Span<int> mb, int channel, JxrBandsPresent bands) {
    if (reader.IsEof)
      return;

    Span<int> dcSubBand = stackalloc int[_DC_SUBBAND_SIZE];
    dcSubBand.Clear();

    // --- DC coefficient ---
    var dcResidual = _vlcEngine.DecodeDc(reader);
    var dcValue = _quantizer.DequantizeDc(dcResidual, channel) + _dcPred[channel];
    _dcPred[channel] = dcValue;
    dcSubBand[0] = dcValue;

    // --- LP coefficients (15 values forming the rest of the 4x4 DC sub-band) ---
    if (bands != JxrBandsPresent.DcOnly) {
      for (var i = 1; i < _DC_SUBBAND_SIZE; ++i) {
        if (reader.IsEof)
          break;

        var lpResidual = _vlcEngine.DecodeLp(reader);
        var lpValue = _quantizer.DequantizeLp(lpResidual, channel);

        // Add left-neighbor LP prediction
        lpValue += _lpPredLeft[channel][i - 1];
        _lpPredLeft[channel][i - 1] = lpValue;

        dcSubBand[i] = lpValue;
      }
    }

    // Inverse DC sub-band transform (converts the 4x4 DC grid back to per-block DC values)
    JxrLbt.InverseDcTransform(dcSubBand);

    // Place DC sub-band values at the [0,0] position of each 4x4 block
    JxrLbt.InsertDcSubBand(dcSubBand, mb);

    // --- HP coefficients (15 per 4x4 block, 16 blocks) ---
    if (bands == JxrBandsPresent.All || bands == JxrBandsPresent.NoFlexbits)
      _DecodeHpBand(reader, mb, channel);

    // Inverse first-stage transform (PCT on each 4x4 block)
    // Then overlap post-filter at internal block boundaries
    JxrLbt.InverseLbt(mb);
  }

  /// <summary>
  /// Decodes the HP band for all 16 blocks within a macroblock.
  /// Uses run-length VLC for efficient coding of sparse HP coefficients.
  /// </summary>
  private void _DecodeHpBand(JxrBitReader reader, Span<int> mb, int channel) {
    Span<int> hpCoeffs = stackalloc int[15];
    var qpHp = _quantizer.GetQpHp(channel);
    var hpContext = new JxrVlcContext(2);

    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx) {
      if (reader.IsEof)
        return;

      hpCoeffs.Clear();
      JxrRunLengthVlc.DecodeHpBlock(reader, hpCoeffs, hpContext);

      // Dequantize and place HP coefficients into the macroblock
      var baseIdx = by * _BLOCK_SIZE * _MB_SIZE + bx * _BLOCK_SIZE;
      var hpIdx = 0;
      for (var r = 0; r < _BLOCK_SIZE; ++r)
      for (var c = 0; c < _BLOCK_SIZE; ++c) {
        if (r == 0 && c == 0)
          continue; // DC/LP position, already filled

        var idx = baseIdx + r * _MB_SIZE + c;
        if (idx < _MB_PIXEL_COUNT && hpIdx < 15) {
          mb[idx] = hpCoeffs[hpIdx] * qpHp;
          ++hpIdx;
        }
      }
    }
  }
}

/// <summary>
/// Macroblock-level encoder for JPEG XR.
/// Transforms, quantizes, and entropy-codes macroblock data for each channel.
/// </summary>
internal sealed class JxrMacroblockEncoder {

  private const int _MB_SIZE = 16;
  private const int _BLOCK_SIZE = 4;
  private const int _BLOCKS_PER_MB = _MB_SIZE / _BLOCK_SIZE;
  private const int _MB_PIXEL_COUNT = _MB_SIZE * _MB_SIZE;
  private const int _DC_SUBBAND_SIZE = _BLOCKS_PER_MB * _BLOCKS_PER_MB;

  private readonly int _channelCount;
  private readonly JxrQuantizer _quantizer;
  private readonly JxrAdaptiveVlcEngine _vlcEngine;

  /// <summary>DC prediction state per channel.</summary>
  private readonly int[] _dcPred;

  /// <summary>LP prediction state per channel.</summary>
  private readonly int[][] _lpPredLeft;

  public JxrMacroblockEncoder(int channelCount, JxrQuantizer quantizer, JxrAdaptiveVlcEngine vlcEngine) {
    _channelCount = Math.Max(channelCount, 1);
    _quantizer = quantizer;
    _vlcEngine = vlcEngine;
    _dcPred = new int[_channelCount];
    _lpPredLeft = new int[_channelCount][];
    for (var c = 0; c < _channelCount; ++c)
      _lpPredLeft[c] = new int[15];
  }

  /// <summary>Resets the prediction state for a new macroblock row.</summary>
  public void ResetRowState() {
    Array.Clear(_dcPred);
    for (var c = 0; c < _channelCount; ++c)
      Array.Clear(_lpPredLeft[c]);
    _vlcEngine.ResetHp();
  }

  /// <summary>
  /// Encodes one macroblock for all channels to the bitstream.
  /// The channel data should be in spatial domain (raw pixel values).
  /// </summary>
  /// <param name="writer">Bit writer for the output bitstream.</param>
  /// <param name="channels">Channel buffers (each 256 elements) in spatial domain.</param>
  /// <param name="bands">Which frequency bands to encode.</param>
  public void EncodeMacroblock(JxrBitWriter writer, int[][] channels, JxrBandsPresent bands) {
    for (var c = 0; c < _channelCount; ++c)
      _EncodeChannelMacroblock(writer, channels[c], c, bands);
  }

  /// <summary>
  /// Encodes one macroblock for a single channel.
  /// Steps: forward LBT -> extract DC sub-band -> forward DC transform -> quantize -> encode DC/LP/HP.
  /// </summary>
  private void _EncodeChannelMacroblock(JxrBitWriter writer, int[] mbData, int channel, JxrBandsPresent bands) {
    // Make a working copy to avoid mutating the input
    Span<int> mb = stackalloc int[_MB_PIXEL_COUNT];
    mbData.AsSpan(0, Math.Min(mbData.Length, _MB_PIXEL_COUNT)).CopyTo(mb);

    // Forward LBT: overlap pre-filter + PCT on each 4x4 block
    JxrLbt.ForwardLbt(mb);

    // Extract the 4x4 DC sub-band
    Span<int> dcSubBand = stackalloc int[_DC_SUBBAND_SIZE];
    JxrLbt.ExtractDcSubBand(mb, dcSubBand);

    // Forward DC sub-band transform
    JxrLbt.ForwardDcTransform(dcSubBand);

    // Quantize DC sub-band
    _quantizer.QuantizeDcSubBand(dcSubBand, channel);

    // --- Encode DC ---
    var dcResidual = dcSubBand[0] - _dcPred[channel];
    _dcPred[channel] = dcSubBand[0];
    _vlcEngine.EncodeDc(writer, dcResidual);

    // --- Encode LP ---
    if (bands != JxrBandsPresent.DcOnly) {
      for (var i = 1; i < _DC_SUBBAND_SIZE; ++i) {
        var lpValue = dcSubBand[i];
        var lpResidual = lpValue - _lpPredLeft[channel][i - 1];
        _lpPredLeft[channel][i - 1] = lpValue;
        _vlcEngine.EncodeLp(writer, lpResidual);
      }
    }

    // --- Encode HP ---
    if (bands == JxrBandsPresent.All || bands == JxrBandsPresent.NoFlexbits) {
      // Quantize HP band in-place
      _quantizer.QuantizeHpBand(mb, channel);

      // Re-insert the (already quantized) DC sub-band so we can properly extract HP
      _quantizer.DequantizeDcSubBand(dcSubBand, channel);
      JxrLbt.InsertDcSubBand(dcSubBand, mb);

      _EncodeHpBand(writer, mb, channel);
    }
  }

  /// <summary>Encodes the HP coefficients for all 16 blocks using run-length VLC.</summary>
  private void _EncodeHpBand(JxrBitWriter writer, ReadOnlySpan<int> mb, int channel) {
    Span<int> hpCoeffs = stackalloc int[15];
    var hpContext = new JxrVlcContext(2);

    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx) {
      var baseIdx = by * _BLOCK_SIZE * _MB_SIZE + bx * _BLOCK_SIZE;
      var hpIdx = 0;

      for (var r = 0; r < _BLOCK_SIZE; ++r)
      for (var c = 0; c < _BLOCK_SIZE; ++c) {
        if (r == 0 && c == 0)
          continue;

        var idx = baseIdx + r * _MB_SIZE + c;
        hpCoeffs[hpIdx] = idx < _MB_PIXEL_COUNT ? mb[idx] : 0;
        ++hpIdx;
      }

      JxrRunLengthVlc.EncodeHpBlock(writer, hpCoeffs, hpContext);
    }
  }
}

/// <summary>Band presence flags indicating which frequency bands are present in the bitstream.</summary>
internal enum JxrBandsPresent : byte {
  /// <summary>DC + LP + HP bands (all coefficient data present).</summary>
  All = 0,

  /// <summary>DC + LP only (no highpass detail).</summary>
  DcLp = 1,

  /// <summary>DC only (thumbnail quality).</summary>
  DcOnly = 2,

  /// <summary>All bands present but without flexbits refinement.</summary>
  NoFlexbits = 3
}
