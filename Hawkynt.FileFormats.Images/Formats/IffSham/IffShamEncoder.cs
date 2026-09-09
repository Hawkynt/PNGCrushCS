using System;
using FileFormat.Core;

namespace FileFormat.IffSham;

/// <summary>Fits RGB pixels to the sixteen changing base colours and HAM6 commands SHAM can display.</summary>
internal static class IffShamEncoder {

  public static IffShamFile Encode(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(IffShamFile.DefaultWidth, IffShamFile.DefaultHeight).EnsureFormat(PixelFormat.Bgra32);
    var pixels = new byte[IffShamFile.DefaultWidth * IffShamFile.DefaultHeight];
    var palettes = new byte[IffShamFile.DefaultHeight * IffShamFile.PaletteBytesPerScanline];
    var row = new byte[IffShamFile.DefaultWidth * 4];

    for (var y = 0; y < IffShamFile.DefaultHeight; ++y) {
      source.PixelData.AsSpan(y * row.Length, row.Length).CopyTo(row);
      var quantized = ColorQuantizer.Quantize(row, IffShamFile.DefaultWidth, IffShamFile.PaletteEntries);
      var palette = palettes.AsSpan(y * IffShamFile.PaletteBytesPerScanline, IffShamFile.PaletteBytesPerScanline);

      // SHAM stores the Amiga's twelve-bit 0RGB register values. Snap the quantizer's eight-bit
      // colours once here so command selection is measured against exactly what the file will carry.
      for (var i = 0; i < IffShamFile.PaletteEntries; ++i) {
        var sourceIndex = Math.Min(i, quantized.Count - 1) * 3;
        palette[i * 3] = _SnapToNibble(quantized.Palette[sourceIndex]);
        palette[i * 3 + 1] = _SnapToNibble(quantized.Palette[sourceIndex + 1]);
        palette[i * 3 + 2] = _SnapToNibble(quantized.Palette[sourceIndex + 2]);
      }

      _EncodeRow(row, palette, pixels.AsSpan(y * IffShamFile.DefaultWidth, IffShamFile.DefaultWidth));
    }

    return new() {
      Width = IffShamFile.DefaultWidth,
      Height = IffShamFile.DefaultHeight,
      RawData = [],
      PixelData = pixels,
      ScanlinePalettes = palettes,
    };
  }

  private static void _EncodeRow(ReadOnlySpan<byte> bgra, ReadOnlySpan<byte> palette, Span<byte> commands) {
    byte heldR = palette[0], heldG = palette[1], heldB = palette[2];

    for (var x = 0; x < commands.Length; ++x) {
      var source = x * 4;
      var targetB = bgra[source];
      var targetG = bgra[source + 1];
      var targetR = bgra[source + 2];

      var bestError = int.MaxValue;
      byte bestCode = 0, bestR = 0, bestG = 0, bestB = 0;

      // Direct palette entries are considered first so an exact base colour wins ties over a hold
      // command. Apart from producing simpler streams, that also re-seeds all three held channels.
      for (var i = 0; i < IffShamFile.PaletteEntries; ++i) {
        var at = i * 3;
        _Consider((byte)i, palette[at], palette[at + 1], palette[at + 2], targetR, targetG, targetB,
          ref bestError, ref bestCode, ref bestR, ref bestG, ref bestB);
      }

      var blueNibble = _ToNibble(targetB);
      var redNibble = _ToNibble(targetR);
      var greenNibble = _ToNibble(targetG);

      _Consider((byte)(0x10 | blueNibble), heldR, heldG, _ExpandNibble(blueNibble), targetR, targetG, targetB,
        ref bestError, ref bestCode, ref bestR, ref bestG, ref bestB);
      _Consider((byte)(0x20 | redNibble), _ExpandNibble(redNibble), heldG, heldB, targetR, targetG, targetB,
        ref bestError, ref bestCode, ref bestR, ref bestG, ref bestB);
      _Consider((byte)(0x30 | greenNibble), heldR, _ExpandNibble(greenNibble), heldB, targetR, targetG, targetB,
        ref bestError, ref bestCode, ref bestR, ref bestG, ref bestB);

      commands[x] = bestCode;
      heldR = bestR;
      heldG = bestG;
      heldB = bestB;
    }
  }

  private static void _Consider(
    byte code, byte red, byte green, byte blue,
    byte targetRed, byte targetGreen, byte targetBlue,
    ref int bestError, ref byte bestCode, ref byte bestRed, ref byte bestGreen, ref byte bestBlue
  ) {
    var dr = red - targetRed;
    var dg = green - targetGreen;
    var db = blue - targetBlue;
    var error = dr * dr + dg * dg + db * db;
    if (error >= bestError)
      return;

    bestError = error;
    bestCode = code;
    bestRed = red;
    bestGreen = green;
    bestBlue = blue;
  }

  internal static byte ToNibble(byte value) => _ToNibble(value);

  private static byte _ToNibble(byte value) => (byte)Math.Min(15, (value + 8) / 17);
  private static byte _ExpandNibble(byte value) => (byte)(value * 0x11);
  private static byte _SnapToNibble(byte value) => _ExpandNibble(_ToNibble(value));
}
