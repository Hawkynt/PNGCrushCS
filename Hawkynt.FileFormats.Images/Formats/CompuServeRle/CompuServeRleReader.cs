using System;
using System.IO;

namespace FileFormat.CompuServeRle;

/// <summary>Reads standard CompuServe RLE terminal graphics.</summary>
public static class CompuServeRleReader {

  private const byte _Escape = 0x1B;

  public static CompuServeRleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CompuServe RLE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CompuServeRleFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[checked((int)(stream.Length - stream.Position))];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static CompuServeRleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CompuServeRleFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 6)
      throw new InvalidDataException("CompuServe RLE data is too short to contain a graphics header and terminator.");
    if (data[0] != _Escape || data[1] != (byte)'G')
      throw new InvalidDataException("Invalid CompuServe RLE graphics-mode header; expected ESC G H or ESC G M.");

    var (width, height) = data[2] switch {
      (byte)'M' => (CompuServeRleFile.MediumWidth, CompuServeRleFile.MediumHeight),
      (byte)'H' => (CompuServeRleFile.HighWidth, CompuServeRleFile.HighHeight),
      _ => throw new InvalidDataException("Unsupported CompuServe RLE graphics mode; expected H or M."),
    };

    var stride = CompuServeRleFile.GetRowStride(width);
    var raster = new byte[checked(stride * height)];
    var totalPixels = checked(width * height);
    var pixel = 0;
    var white = false;
    var terminated = false;
    var position = 3;

    while (position < data.Length) {
      var value = data[position++];
      if (value == _Escape) {
        if (position + 1 >= data.Length || data[position] != (byte)'G' || data[position + 1] != (byte)'N')
          throw new InvalidDataException("Invalid or truncated CompuServe RLE graphics terminator; expected ESC G N.");
        position += 2;
        terminated = true;
        break;
      }

      // Terminal controls do not consume pixels or change the alternating run colour. BEL may
      // appear immediately before ESC G N. DEL is the historical exception: some encoders used
      // 0x7F as a 95-pixel run, while more terminal-safe encoders cap themselves at '~' / 94.
      if (value < 0x20)
        continue;
      if (value > 0x7F)
        throw new InvalidDataException($"Invalid CompuServe RLE run byte 0x{value:X2}; run data must remain 7-bit.");

      var count = value - 0x20;
      if (pixel + count > totalPixels)
        throw new InvalidDataException("CompuServe RLE run expands beyond the selected graphics mode.");

      if (white)
        _SetWhiteRun(raster, width, pixel, count);

      pixel += count;
      white = !white;
    }

    if (!terminated)
      throw new InvalidDataException("CompuServe RLE stream is missing the ESC G N graphics terminator.");
    if (pixel != totalPixels)
      throw new InvalidDataException($"CompuServe RLE stream expands to {pixel} pixels; expected exactly {totalPixels}.");

    // A captured terminal stream may leave CR/LF/BEL-style controls after graphics mode ends, but
    // other bytes are unrelated payload and are rejected rather than silently discarded.
    for (; position < data.Length; ++position)
      if (data[position] >= 0x20)
        throw new InvalidDataException("Unexpected data follows the CompuServe RLE graphics terminator.");

    return new() {
      Width = width,
      Height = height,
      RasterData = raster,
    };
  }

  private static void _SetWhiteRun(Span<byte> raster, int width, int startPixel, int count) {
    var stride = CompuServeRleFile.GetRowStride(width);
    for (var i = 0; i < count; ++i) {
      var absolute = startPixel + i;
      var y = absolute / width;
      var x = absolute - y * width;
      raster[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }
  }
}
