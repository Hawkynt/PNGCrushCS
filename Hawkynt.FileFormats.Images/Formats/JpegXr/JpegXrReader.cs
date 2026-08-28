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
    uint imageOffset = 0, imageByteCount = 0;
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
      }
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid JPEG XR dimensions: {width}x{height}.");
    if (pixelFormat is null)
      throw new InvalidDataException("JPEG XR container is missing the required 16-byte pixel-format GUID.");
    if (pixelFormat.Value.HasAlpha)
      throw new NotSupportedException("The current JpegXrFile model exposes Gray8 and RGB24 only; planar/interleaved alpha is not representable yet.");
    if (pixelFormat.Value.ComponentCount is not (1 or 3))
      throw new NotSupportedException($"JPEG XR pixel format has {pixelFormat.Value.ComponentCount} components; the current model supports one or three.");
    if (imageOffset == 0 || imageByteCount == 0)
      throw new InvalidDataException("JPEG XR container is missing its image codestream location.");

    var offset = checked((int)imageOffset);
    var count = checked((int)imageByteCount);
    if (offset < 0 || count < 0 || offset > data.Length - count)
      throw new InvalidDataException($"JPEG XR codestream range [{offset}, {offset + (long)count}) lies outside the file ({data.Length} bytes).");

    var codestream = data.AsSpan(offset, count);
    byte[] pixels;

    if (pixelFormat.Value.ComponentCount == 1) {
      var decoded = JxrCodestream.DecodeGray(codestream);
      _RequireMatchingDimensions(width, height, decoded.width, decoded.height);
      pixels = new byte[checked(width * height)];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = checked((byte)decoded.y[i]);
    } else {
      var decoded = JxrCodestream.Decode(codestream);
      _RequireMatchingDimensions(width, height, decoded.width, decoded.height);
      var pixelCount = checked(width * height);
      pixels = new byte[checked(pixelCount * 3)];
      for (var i = 0; i < pixelCount; ++i) {
        var destination = i * 3;
        pixels[destination] = checked((byte)decoded.r[i]);
        pixels[destination + 1] = checked((byte)decoded.g[i]);
        pixels[destination + 2] = checked((byte)decoded.b[i]);
      }
    }

    return new() {
      Width = width,
      Height = height,
      ComponentCount = pixelFormat.Value.ComponentCount,
      PixelData = pixels,
    };
  }

  private static void _RequireMatchingDimensions(int containerWidth, int containerHeight, int codecWidth, int codecHeight) {
    if (containerWidth != codecWidth || containerHeight != codecHeight)
      throw new InvalidDataException(
        $"JPEG XR container dimensions {containerWidth}x{containerHeight} disagree with codestream dimensions {codecWidth}x{codecHeight}.");
  }
}
