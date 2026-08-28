using System;
using System.IO;
using SharpAstro.Jxr;

namespace FileFormat.JpegXr;

/// <summary>Reads JPEG XR (T.833 container + T.832 codestream) files.</summary>
public static class JpegXrReader {

  private const int _MIN_FILE_SIZE = 14;

  /// <summary>The little-endian word represented by the on-disk bytes BC 01.</summary>
  internal const ushort JPEGXR_MAGIC = 0x01BC;

  public static JpegXrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG XR file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegXrFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static JpegXrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JpegXrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid JPEG XR file.");
    if (data[0] != (byte)'I' || data[1] != (byte)'I')
      throw new InvalidDataException($"Invalid JPEG XR byte order: expected 'II', got 0x{data[0]:X2} 0x{data[1]:X2}.");

    var header = JpegXrHeader.ReadFrom(data);
    if (header.Magic != JPEGXR_MAGIC)
      throw new InvalidDataException($"Invalid JPEG XR magic: expected 0x{JPEGXR_MAGIC:X4}, got 0x{header.Magic:X4}.");

    var ifdOffset = checked((int)header.IfdOffset);
    if (ifdOffset < 8 || ifdOffset > data.Length - 2)
      throw new InvalidDataException($"Invalid IFD offset: {ifdOffset}.");

    return _ParseIfd(data.ToArray(), ifdOffset);
  }

  private static JpegXrFile _ParseIfd(byte[] data, int ifdOffset) {
    var entries = JpegXrIfd.ParseEntries(data, ifdOffset);

    int width = 0, height = 0;
    uint imageOffset = 0, imageByteCount = 0, alphaOffset = 0, alphaByteCount = 0;
    JpegXrPixelFormatInfo? pixelFormat = null;

    foreach (var entry in entries) {
      switch (entry.Tag) {
        case JpegXrIfd.TAG_PIXEL_FORMAT:
          pixelFormat = JpegXrIfd.ParsePixelFormat(data, entry);
          break;
        case JpegXrIfd.TAG_IMAGE_WIDTH:
          width = checked((int)entry.Value);
          break;
        case JpegXrIfd.TAG_IMAGE_HEIGHT:
          height = checked((int)entry.Value);
          break;
        case JpegXrIfd.TAG_IMAGE_OFFSET:
          imageOffset = entry.Value;
          break;
        case JpegXrIfd.TAG_IMAGE_BYTE_COUNT:
          imageByteCount = entry.Value;
          break;
        case JpegXrIfd.TAG_ALPHA_OFFSET:
          alphaOffset = entry.Value;
          break;
        case JpegXrIfd.TAG_ALPHA_BYTE_COUNT:
          alphaByteCount = entry.Value;
          break;
      }
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid JPEG XR dimensions: {width}x{height}.");
    if (pixelFormat is null)
      throw new InvalidDataException("JPEG XR container is missing the required 16-byte pixel-format GUID.");
    if (pixelFormat.Value.ComponentCount is not (1 or 3 or 4))
      throw new NotSupportedException($"JPEG XR pixel format has {pixelFormat.Value.ComponentCount} components; the current model supports one, three, or four.");
    if (imageOffset == 0 || imageByteCount == 0)
      throw new InvalidDataException("JPEG XR container is missing its image codestream location.");

    var codestream = _SliceCodestream(data, imageOffset, imageByteCount, "image");
    var pixelCount = checked(width * height);
    byte[] pixels;

    if (pixelFormat.Value.ComponentCount == 1) {
      var decoded = JxrCodestream.DecodeGray(codestream);
      _RequireMatchingDimensions(width, height, decoded.width, decoded.height, "image");
      pixels = new byte[pixelCount];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = checked((byte)decoded.y[i]);
    } else {
      var decoded = JxrCodestream.Decode(codestream);
      _RequireMatchingDimensions(width, height, decoded.width, decoded.height, "image");

      if (!pixelFormat.Value.HasAlpha) {
        pixels = new byte[checked(pixelCount * 3)];
        for (var i = 0; i < pixelCount; ++i) {
          var destination = i * 3;
          pixels[destination] = checked((byte)decoded.r[i]);
          pixels[destination + 1] = checked((byte)decoded.g[i]);
          pixels[destination + 2] = checked((byte)decoded.b[i]);
        }
      } else {
        if (alphaOffset == 0 || alphaByteCount == 0)
          throw new NotSupportedException("JPEG XR interleaved alpha is not exposed by the current T.832 adapter; a planar BCC2/BCC3 alpha codestream is required.");

        var alpha = JpegXrFrequencyGrayDecoder.Decode(_SliceCodestream(data, alphaOffset, alphaByteCount, "alpha"));
        _RequireMatchingDimensions(width, height, alpha.width, alpha.height, "alpha");
        pixels = new byte[checked(pixelCount * 4)];
        for (var i = 0; i < pixelCount; ++i) {
          var a = checked((byte)alpha.y[i]);
          var r = checked((byte)decoded.r[i]);
          var g = checked((byte)decoded.g[i]);
          var b = checked((byte)decoded.b[i]);
          if (pixelFormat.Value.PremultipliedAlpha && a is > 0 and < 255) {
            r = _Unpremultiply(r, a);
            g = _Unpremultiply(g, a);
            b = _Unpremultiply(b, a);
          }

          var destination = i * 4;
          pixels[destination] = r;
          pixels[destination + 1] = g;
          pixels[destination + 2] = b;
          pixels[destination + 3] = a;
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      ComponentCount = pixelFormat.Value.ComponentCount,
      PixelData = pixels,
    };
  }

  private static ReadOnlySpan<byte> _SliceCodestream(byte[] data, uint offsetValue, uint countValue, string name) {
    var offset = checked((int)offsetValue);
    var count = checked((int)countValue);
    if (offset < 0 || count < 0 || offset > data.Length - count)
      throw new InvalidDataException($"JPEG XR {name} codestream range [{offset}, {offset + (long)count}) lies outside the file ({data.Length} bytes).");
    return data.AsSpan(offset, count);
  }

  private static void _RequireMatchingDimensions(int containerWidth, int containerHeight, int codecWidth, int codecHeight, string plane) {
    if (containerWidth != codecWidth || containerHeight != codecHeight)
      throw new InvalidDataException(
        $"JPEG XR container dimensions {containerWidth}x{containerHeight} disagree with {plane} codestream dimensions {codecWidth}x{codecHeight}.");
  }

  private static byte _Unpremultiply(byte value, byte alpha)
    => (byte)Math.Clamp((value * 255 + alpha / 2) / alpha, 0, 255);
}
