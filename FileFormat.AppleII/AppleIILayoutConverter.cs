using System;

namespace FileFormat.AppleII;

/// <summary>Converts between Apple II interleaved memory order and linear scanline order.</summary>
internal static class AppleIILayoutConverter {

  /// <summary>Number of scanlines.</summary>
  private const int _ROW_COUNT = 192;

  /// <summary>Bytes per scanline for HGR (280 pixels / 7 pixels per byte = 40 bytes).</summary>
  private const int _HGR_BYTES_PER_LINE = 40;

  /// <summary>HGR bank size in bytes.</summary>
  private const int _HGR_BANK_SIZE = 8192;

  /// <summary>Computes the interleaved memory offset for a given scanline.</summary>
  private static int _GetInterleavedOffset(int line)
    => (line % 8) * 1024 + ((line / 8) % 8) * 128 + (line / 64) * 40;

  /// <summary>Converts interleaved Apple II memory order to linear scanline order.</summary>
  internal static byte[] Deinterleave(byte[] rawData, AppleIIMode mode) {
    var bytesPerLine = mode == AppleIIMode.Dhgr ? _HGR_BYTES_PER_LINE * 2 : _HGR_BYTES_PER_LINE;
    var linearData = new byte[_ROW_COUNT * bytesPerLine];

    for (var line = 0; line < _ROW_COUNT; ++line) {
      var interleavedOffset = _GetInterleavedOffset(line);
      var linearOffset = line * bytesPerLine;

      if (mode == AppleIIMode.Dhgr) {
        // DHGR: aux bank first, then main bank
        Array.Copy(rawData, interleavedOffset, linearData, linearOffset, _HGR_BYTES_PER_LINE);
        Array.Copy(rawData, _HGR_BANK_SIZE + interleavedOffset, linearData, linearOffset + _HGR_BYTES_PER_LINE, _HGR_BYTES_PER_LINE);
      } else
        Array.Copy(rawData, interleavedOffset, linearData, linearOffset, _HGR_BYTES_PER_LINE);
    }

    return linearData;
  }

  /// <summary>Converts linear scanline order back to interleaved Apple II memory order.</summary>
  internal static byte[] Interleave(byte[] linearData, AppleIIMode mode) {
    var bytesPerLine = mode == AppleIIMode.Dhgr ? _HGR_BYTES_PER_LINE * 2 : _HGR_BYTES_PER_LINE;
    var bankCount = mode == AppleIIMode.Dhgr ? 2 : 1;
    var result = new byte[_HGR_BANK_SIZE * bankCount];

    for (var line = 0; line < _ROW_COUNT; ++line) {
      var interleavedOffset = _GetInterleavedOffset(line);
      var linearOffset = line * bytesPerLine;

      if (mode == AppleIIMode.Dhgr) {
        // DHGR: aux bank first, then main bank
        Array.Copy(linearData, linearOffset, result, interleavedOffset, _HGR_BYTES_PER_LINE);
        Array.Copy(linearData, linearOffset + _HGR_BYTES_PER_LINE, result, _HGR_BANK_SIZE + interleavedOffset, _HGR_BYTES_PER_LINE);
      } else
        Array.Copy(linearData, linearOffset, result, interleavedOffset, _HGR_BYTES_PER_LINE);
    }

    return result;
  }
}
