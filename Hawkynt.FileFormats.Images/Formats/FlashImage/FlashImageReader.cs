using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.FlashImage;

/// <summary>Reads Flash Image pictures (.fi) from bytes, streams, or file paths.</summary>
public static class FlashImageReader {

  /// <summary>The largest side taken, so a corrupt header cannot ask for an absurd allocation.</summary>
  private const int _MaximumSide = 32767;

  public static FlashImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Flash Image picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FlashImageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static FlashImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static FlashImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FlashImageFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a Flash Image picture (need at least {FlashImageFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..FlashImageFile.Magic.Length].SequenceEqual(FlashImageFile.Magic))
      throw new InvalidDataException("Not a Flash Image picture: the four bytes it opens with are not the ones this format uses.");

    var width = _ReadWord(data, 4);
    var height = _ReadWord(data, 6);
    var mode = _ReadWord(data, 8);
    var paletteCount = _ReadWord(data, 14);

    // Modes one and two do not describe the picture in the header at all: the reader skips to the
    // JPEG stream and lets that stream state its own size.
    if (mode is 1 or 2) {
      if (data.Length <= FlashImageFile.JpegPayloadOffset)
        throw new InvalidDataException($"A mode {mode} Flash Image carries its JPEG at offset {FlashImageFile.JpegPayloadOffset} and the file is only {data.Length} bytes.");

      var jpeg = data[FlashImageFile.JpegPayloadOffset..].ToArray();
      return new() {
        Width = width,
        Height = height,
        Mode = mode,
        Palette = [],
        PaletteCount = 0,
        PixelData = [],
        JpegData = jpeg,
      };
    }

    if (width is < 1 or > _MaximumSide || height is < 1 or > _MaximumSide)
      throw new InvalidDataException($"Invalid Flash Image dimensions: {width}x{height}.");

    if (paletteCount > 256)
      throw new InvalidDataException($"A Flash Image palette of {paletteCount} entries is more than the 256 an eight bit picture can index.");

    var stride = FlashImageFile.RowStride(width);
    var paletteBytes = paletteCount * 3;
    var expected = paletteBytes + stride * height;
    var inflated = _Inflate(data[FlashImageFile.HeaderSize..], expected);
    if (inflated.Length < expected)
      throw new InvalidDataException($"A {width}x{height} Flash Image with {paletteCount} colours needs {expected} bytes behind the deflate stream and only {inflated.Length} came out of it.");

    // XnView hands its picture builder 256 entries whatever the header's count says, taking them
    // from the front of the inflated bytes, so a file whose count is short and whose indices are
    // not simply reads on into the rows. Keeping all 256 here reproduces that and stops a short
    // count from putting an index out of reach of the palette.
    var palette = new byte[FlashImageFile.FullPaletteBytes];
    inflated.AsSpan(0, Math.Min(inflated.Length, palette.Length)).CopyTo(palette);

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      inflated.AsSpan(paletteBytes + y * stride, width).CopyTo(pixels.AsSpan(y * width));

    return new() {
      Width = width,
      Height = height,
      Mode = mode,
      Palette = palette,
      PaletteCount = paletteCount,
      PixelData = pixels,
    };
  }

  /// <summary>Runs the payload through zlib, which is what XnView's own three entry points do.</summary>
  private static byte[] _Inflate(ReadOnlySpan<byte> payload, int expected) {
    var input = new MemoryStream(payload.ToArray(), false);
    var output = new MemoryStream(expected);
    try {
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      zlib.CopyTo(output);
    } catch (InvalidDataException e) {
      throw new InvalidDataException("The Flash Image payload is not a zlib stream.", e);
    }

    return output.ToArray();
  }

  private static int _ReadWord(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];
}
