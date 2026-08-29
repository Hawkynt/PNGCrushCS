using System;
using System.Buffers.Binary;

namespace FileFormat.Apx;

/// <summary>Writes the verified one-layer Ability Photopaint layout with uncompressed 32-bit pixels.</summary>
public static class ApxWriter {

  private const int _PixelOffset = 153;

  public static byte[] ToBytes(ApxFile file) {
    if (file.Width is < 1 or > ApxFile.MaximumSide || file.Height is < 1 or > ApxFile.MaximumSide)
      throw new ArgumentException($"APX dimensions must be 1..{ApxFile.MaximumSide}; got {file.Width}x{file.Height}.", nameof(file));
    var expected = checked(file.Width * file.Height * ApxFile.BytesPerPixel);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"APX needs {expected} RGBA bytes.", nameof(file));

    var output = new byte[checked(_PixelOffset + expected)];
    (file.IsPro ? ApxFile.MagicPaintPro : ApxFile.MagicPaint).CopyTo(output);

    // Bytes 21..32 are three unread words. a=b=0 at 33 and 37 means the reader only takes its
    // documented constant 40-byte step, landing at byte 81.
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(33, 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(37, 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(81, 4), checked((uint)Math.Max(0, file.Resolution)));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(85, 4), checked((uint)file.Width));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(89, 4), checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(93, 4), 1); // one layer

    // 97..120 are the two unread words plus the fixed 16-byte layer-table gap. The one layer record
    // starts at 121: four unread words, then a zero-length name word at 137, then three unread words.
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(137, 4), 0);

    var stride = file.Width * 4;
    for (var y = 0; y < file.Height; ++y) {
      var fromRow = y * stride;
      var toRow = _PixelOffset + (file.Height - 1 - y) * stride;
      for (var x = 0; x < file.Width; ++x) {
        var from = fromRow + x * 4;
        var to = toRow + x * 4;
        // library RGBA -> on-disk A,B,G,R
        output[to] = file.PixelData[from + 3];
        output[to + 1] = file.PixelData[from + 2];
        output[to + 2] = file.PixelData[from + 1];
        output[to + 3] = file.PixelData[from];
      }
    }

    return output;
  }
}
