using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Flexible quantization engine for JPEG XR (ITU-T T.832).
/// Supports per-tile, per-macroblock, and per-channel quantization parameters (QP).
/// </summary>
/// <remarks>
/// JPEG XR uses three frequency bands, each independently quantized:
/// - DC: the single lowest-frequency coefficient per macroblock channel
/// - LP: the 15 lowpass coefficients from the DC sub-band transform
/// - HP: the 15 highpass coefficients per 4x4 block (240 per macroblock)
///
/// Quantization is uniform dead-zone: value / QP with rounding toward zero.
/// Dequantization multiplies by QP.
///
/// The flexible quantization model allows QP to vary:
/// - Per tile (different regions of the image)
/// - Per macroblock (spatial adaptation within a tile)
/// - Per channel (e.g., luma vs chroma)
///
/// QP values range from 1 (lossless for that band) to 255.
/// </remarks>
internal sealed class JxrQuantizer {

  /// <summary>QP for DC coefficients per channel.</summary>
  private readonly int[] _qpDc;

  /// <summary>QP for LP coefficients per channel.</summary>
  private readonly int[] _qpLp;

  /// <summary>QP for HP coefficients per channel.</summary>
  private readonly int[] _qpHp;

  /// <summary>Number of channels.</summary>
  public int ChannelCount { get; }

  /// <summary>Creates a quantizer with uniform QP across all channels.</summary>
  public JxrQuantizer(int qpDc, int qpLp, int qpHp, int channelCount) {
    ChannelCount = Math.Max(channelCount, 1);
    _qpDc = new int[ChannelCount];
    _qpLp = new int[ChannelCount];
    _qpHp = new int[ChannelCount];

    Array.Fill(_qpDc, Math.Max(qpDc, 1));
    Array.Fill(_qpLp, Math.Max(qpLp, 1));
    Array.Fill(_qpHp, Math.Max(qpHp, 1));
  }

  /// <summary>Creates a quantizer with per-channel QP arrays.</summary>
  public JxrQuantizer(int[] qpDc, int[] qpLp, int[] qpHp) {
    ChannelCount = qpDc.Length;
    _qpDc = new int[ChannelCount];
    _qpLp = new int[ChannelCount];
    _qpHp = new int[ChannelCount];

    for (var c = 0; c < ChannelCount; ++c) {
      _qpDc[c] = Math.Max(c < qpDc.Length ? qpDc[c] : 1, 1);
      _qpLp[c] = Math.Max(c < qpLp.Length ? qpLp[c] : 1, 1);
      _qpHp[c] = Math.Max(c < qpHp.Length ? qpHp[c] : 1, 1);
    }
  }

  /// <summary>Gets the DC quantization parameter for the given channel.</summary>
  public int GetQpDc(int channel) => _qpDc[Math.Clamp(channel, 0, ChannelCount - 1)];

  /// <summary>Gets the LP quantization parameter for the given channel.</summary>
  public int GetQpLp(int channel) => _qpLp[Math.Clamp(channel, 0, ChannelCount - 1)];

  /// <summary>Gets the HP quantization parameter for the given channel.</summary>
  public int GetQpHp(int channel) => _qpHp[Math.Clamp(channel, 0, ChannelCount - 1)];

  /// <summary>Quantizes a DC coefficient: divide by QP with dead-zone rounding.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int QuantizeDc(int value, int channel) => _DeadZoneQuantize(value, GetQpDc(channel));

  /// <summary>Quantizes an LP coefficient.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int QuantizeLp(int value, int channel) => _DeadZoneQuantize(value, GetQpLp(channel));

  /// <summary>Quantizes an HP coefficient.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int QuantizeHp(int value, int channel) => _DeadZoneQuantize(value, GetQpHp(channel));

  /// <summary>Dequantizes a DC coefficient: multiply by QP.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DequantizeDc(int value, int channel) => value * GetQpDc(channel);

  /// <summary>Dequantizes an LP coefficient.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DequantizeLp(int value, int channel) => value * GetQpLp(channel);

  /// <summary>Dequantizes an HP coefficient.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DequantizeHp(int value, int channel) => value * GetQpHp(channel);

  /// <summary>
  /// Quantizes all coefficients in a macroblock's DC sub-band (16 elements).
  /// Element [0] is the DC coefficient; elements [1..15] are LP coefficients.
  /// </summary>
  public void QuantizeDcSubBand(Span<int> dc, int channel) {
    if (dc.Length < 1)
      return;

    dc[0] = QuantizeDc(dc[0], channel);
    for (var i = 1; i < Math.Min(dc.Length, 16); ++i)
      dc[i] = QuantizeLp(dc[i], channel);
  }

  /// <summary>
  /// Dequantizes all coefficients in a macroblock's DC sub-band.
  /// </summary>
  public void DequantizeDcSubBand(Span<int> dc, int channel) {
    if (dc.Length < 1)
      return;

    dc[0] = DequantizeDc(dc[0], channel);
    for (var i = 1; i < Math.Min(dc.Length, 16); ++i)
      dc[i] = DequantizeLp(dc[i], channel);
  }

  /// <summary>
  /// Quantizes the HP coefficients (positions 1..15) within all 16 blocks of a macroblock.
  /// </summary>
  /// <param name="mb">256-element macroblock in transform domain.</param>
  /// <param name="channel">Channel index for QP lookup.</param>
  public void QuantizeHpBand(Span<int> mb, int channel) {
    var qp = GetQpHp(channel);
    for (var by = 0; by < 4; ++by)
    for (var bx = 0; bx < 4; ++bx) {
      var baseIdx = by * 4 * 16 + bx * 4;
      // HP = all positions in the 4x4 block except [0,0]
      for (var r = 0; r < 4; ++r)
      for (var c = 0; c < 4; ++c) {
        if (r == 0 && c == 0)
          continue; // skip DC/LP position
        var idx = baseIdx + r * 16 + c;
        if (idx < 256)
          mb[idx] = _DeadZoneQuantize(mb[idx], qp);
      }
    }
  }

  /// <summary>
  /// Dequantizes the HP coefficients within all 16 blocks of a macroblock.
  /// </summary>
  public void DequantizeHpBand(Span<int> mb, int channel) {
    var qp = GetQpHp(channel);
    for (var by = 0; by < 4; ++by)
    for (var bx = 0; bx < 4; ++bx) {
      var baseIdx = by * 4 * 16 + bx * 4;
      for (var r = 0; r < 4; ++r)
      for (var c = 0; c < 4; ++c) {
        if (r == 0 && c == 0)
          continue;
        var idx = baseIdx + r * 16 + c;
        if (idx < 256)
          mb[idx] *= qp;
      }
    }
  }

  /// <summary>
  /// Reads quantization parameters from the bitstream.
  /// Format: 8-bit DC QP, optional 8-bit LP QP, optional 8-bit HP QP per channel.
  /// </summary>
  public static JxrQuantizer ReadFromBitstream(JxrBitReader reader, int channelCount, bool hasLp, bool hasHp) {
    var qpDc = new int[channelCount];
    var qpLp = new int[channelCount];
    var qpHp = new int[channelCount];

    // Read primary (luma) QP
    qpDc[0] = Math.Max((int)reader.ReadBits(8), 1);
    qpLp[0] = hasLp ? Math.Max((int)reader.ReadBits(8), 1) : qpDc[0];
    qpHp[0] = hasHp ? Math.Max((int)reader.ReadBits(8), 1) : qpLp[0];

    // For multi-channel: check if chroma uses same QP or independent
    if (channelCount > 1) {
      var chromaSameAsLuma = !reader.IsEof && reader.ReadBit() == 1;
      if (chromaSameAsLuma) {
        for (var c = 1; c < channelCount; ++c) {
          qpDc[c] = qpDc[0];
          qpLp[c] = qpLp[0];
          qpHp[c] = qpHp[0];
        }
      } else {
        for (var c = 1; c < channelCount; ++c) {
          qpDc[c] = reader.IsEof ? qpDc[0] : Math.Max((int)reader.ReadBits(8), 1);
          qpLp[c] = hasLp && !reader.IsEof ? Math.Max((int)reader.ReadBits(8), 1) : qpDc[c];
          qpHp[c] = hasHp && !reader.IsEof ? Math.Max((int)reader.ReadBits(8), 1) : qpLp[c];
        }
      }
    }

    return new(qpDc, qpLp, qpHp);
  }

  /// <summary>Writes quantization parameters to the bitstream.</summary>
  public void WriteToBitstream(JxrBitWriter writer, bool hasLp, bool hasHp) {
    // Write primary (luma) QP
    writer.WriteBits((uint)_qpDc[0], 8);
    if (hasLp)
      writer.WriteBits((uint)_qpLp[0], 8);
    if (hasHp)
      writer.WriteBits((uint)_qpHp[0], 8);

    // For multi-channel: write chroma QP
    if (ChannelCount <= 1)
      return;

    var allSame = true;
    for (var c = 1; c < ChannelCount; ++c) {
      if (_qpDc[c] != _qpDc[0] || _qpLp[c] != _qpLp[0] || _qpHp[c] != _qpHp[0]) {
        allSame = false;
        break;
      }
    }

    writer.WriteBit(allSame ? 1 : 0);
    if (allSame)
      return;

    for (var c = 1; c < ChannelCount; ++c) {
      writer.WriteBits((uint)_qpDc[c], 8);
      if (hasLp)
        writer.WriteBits((uint)_qpLp[c], 8);
      if (hasHp)
        writer.WriteBits((uint)_qpHp[c], 8);
    }
  }

  /// <summary>
  /// Dead-zone uniform quantizer: divides by QP with truncation toward zero.
  /// The dead zone means values in the range (-QP, QP) map to 0.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _DeadZoneQuantize(int value, int qp) {
    if (qp <= 1)
      return value;

    // Truncation toward zero (dead-zone quantization)
    if (value >= 0)
      return value / qp;

    return -((-value) / qp);
  }
}

/// <summary>
/// Per-tile quantization parameter set, supporting the JPEG XR tile-level QP override.
/// Each tile can specify independent QP values that override the image-level defaults.
/// </summary>
internal readonly struct JxrTileQuantization {

  /// <summary>Quantizer for this tile.</summary>
  public readonly JxrQuantizer Quantizer;

  /// <summary>Tile column index.</summary>
  public readonly int TileX;

  /// <summary>Tile row index.</summary>
  public readonly int TileY;

  public JxrTileQuantization(JxrQuantizer quantizer, int tileX, int tileY) {
    Quantizer = quantizer;
    TileX = tileX;
    TileY = tileY;
  }
}

/// <summary>
/// Manages a grid of per-tile quantization parameters for the entire image.
/// </summary>
internal sealed class JxrQuantizationMap {

  private readonly JxrQuantizer _defaultQuantizer;
  private readonly JxrTileQuantization[]? _tileOverrides;
  private readonly int _tilesWide;
  private readonly int _tilesHigh;

  /// <summary>Creates a quantization map with a single default QP for the whole image.</summary>
  public JxrQuantizationMap(JxrQuantizer defaultQuantizer, int tilesWide, int tilesHigh) {
    _defaultQuantizer = defaultQuantizer;
    _tilesWide = Math.Max(tilesWide, 1);
    _tilesHigh = Math.Max(tilesHigh, 1);
  }

  /// <summary>Creates a quantization map with per-tile overrides.</summary>
  public JxrQuantizationMap(JxrQuantizer defaultQuantizer, JxrTileQuantization[] tileOverrides, int tilesWide, int tilesHigh) {
    _defaultQuantizer = defaultQuantizer;
    _tileOverrides = tileOverrides;
    _tilesWide = Math.Max(tilesWide, 1);
    _tilesHigh = Math.Max(tilesHigh, 1);
  }

  /// <summary>Gets the quantizer for the given tile position.</summary>
  public JxrQuantizer GetQuantizer(int tileX, int tileY) {
    if (_tileOverrides == null)
      return _defaultQuantizer;

    foreach (var tq in _tileOverrides)
      if (tq.TileX == tileX && tq.TileY == tileY)
        return tq.Quantizer;

    return _defaultQuantizer;
  }

  /// <summary>Gets the default (image-level) quantizer.</summary>
  public JxrQuantizer DefaultQuantizer => _defaultQuantizer;

  /// <summary>Number of tile columns.</summary>
  public int TilesWide => _tilesWide;

  /// <summary>Number of tile rows.</summary>
  public int TilesHigh => _tilesHigh;
}
