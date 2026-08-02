using System;
using System.IO;
using FileFormat.JpegXr.Codec;

namespace FileFormat.JpegXr;

/// <summary>Reads JPEG XR files from bytes, streams, or file paths.</summary>
public static class JpegXrReader {

  /// <summary>Minimum valid file size: 8-byte header + 2-byte IFD count + 4-byte next IFD offset.</summary>
  private const int _MIN_FILE_SIZE = 14;

  /// <summary>JPEG XR magic number (replaces TIFF's 0x002A).</summary>
  /// <summary>
  /// The two bytes after the byte order, as the little-endian word they form.
  /// </summary>
  /// <remarks>
  /// A real file has 0xBC then 0x01, and the container states itself little-endian — so the word is
  /// 0x01BC. It was written here as 0xBC01, which puts the bytes the other way round; the writer
  /// made the same swap, so the two agreed with each other and no file from anywhere else would open.
  /// </remarks>
  internal const ushort JPEGXR_MAGIC = 0x01BC;

  /// <summary>
  /// KNOWN INCOMPLETE, past the magic. A real file now gets as far as the directory and stops there
  /// for want of the tag naming where its image data begins — the container is right and what it
  /// points at is not yet read. What follows is a working subset rather than the whole of JPEG XR.
  /// </summary>

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

    // Validate byte order marker: must be "II" (little-endian)
    if (data[0] != (byte)'I' || data[1] != (byte)'I')
      throw new InvalidDataException($"Invalid JPEG XR byte order: expected 'II', got 0x{data[0]:X2} 0x{data[1]:X2}.");

    var header = JpegXrHeader.ReadFrom(data);

    // Validate magic number
    if (header.Magic != JPEGXR_MAGIC)
      throw new InvalidDataException($"Invalid JPEG XR magic: expected 0x{JPEGXR_MAGIC:X4}, got 0x{header.Magic:X4}.");

    // Read IFD offset
    var ifdOffset = (int)header.IfdOffset;
    if (ifdOffset < 8 || ifdOffset + 2 > data.Length)
      throw new InvalidDataException($"Invalid IFD offset: {ifdOffset}.");

    return _ParseIfd(data.ToArray(), ifdOffset);
  }

  private static JpegXrFile _ParseIfd(byte[] data, int ifdOffset) {
    var entries = JpegXrIfd.ParseEntries(data, ifdOffset);

    int width = 0, height = 0;
    uint imageOffset = 0, imageByteCount = 0;
    var componentCount = 0;

    foreach (var entry in entries) {
      switch (entry.Tag) {
        case JpegXrIfd.TAG_IMAGE_WIDTH:
          width = (int)entry.Value;
          break;
        case JpegXrIfd.TAG_IMAGE_HEIGHT:
          height = (int)entry.Value;
          break;
        case JpegXrIfd.TAG_IMAGE_OFFSET:
          imageOffset = entry.Value;
          break;
        case JpegXrIfd.TAG_IMAGE_BYTE_COUNT:
          imageByteCount = entry.Value;
          break;
        case JpegXrIfd.TAG_PIXEL_FORMAT:
          componentCount = _ParsePixelFormat(data, entry);
          break;
      }
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid JPEG XR dimensions: {width}x{height}.");

    if (imageOffset == 0)
      throw new InvalidDataException("Missing image data offset.");

    if (componentCount == 0)
      componentCount = _InferComponentCount(width, height, imageByteCount);

    var srcOffset = (int)imageOffset;
    var availableBytes = (int)Math.Min(imageByteCount, data.Length - srcOffset);
    if (availableBytes <= 0)
      return new JpegXrFile {
        Width = width,
        Height = height,
        ComponentCount = componentCount,
        PixelData = new byte[width * height * componentCount]
      };

    var compressedData = new byte[availableBytes];
    data.AsSpan(srcOffset, availableBytes).CopyTo(compressedData);

    // Decode compressed bitstream using the JPEG XR codec
    var pixelData = JxrDecoder.Decode(compressedData, width, height, componentCount);

    // The codec runs to completion on both real samples here and draws neither of them: against
    // XnView's rendering the mean error a channel is 117 and 83 out of 255, which is not two lossy
    // decoders differing but a picture that is not the one in the file. Until it is right, saying so
    // is better than handing back a plausible-looking wash of colour nothing downstream can question.
    //
    // The container above this is sound and was worth fixing on its own: the four tags naming where
    // the picture begins were written 0xBCE0 to 0xBCE3, where the standard puts them at 0xBCC0 to
    // 0xBCC3, so no JPEG XR was ever found at all.
    throw new NotSupportedException(
      $"A JPEG XR of {width}x{height} was recognised, but the codec here does not reproduce the picture and its output is not returned.");

    return new JpegXrFile {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      PixelData = pixelData
    };
  }

  /// <summary>Parses the pixel format from the IFD entry. The entry value may be a direct byte or an offset to GUID bytes.</summary>
  private static int _ParsePixelFormat(byte[] data, JpegXrIfdEntry entry) {
    // If count == 1 and type is BYTE, the value field contains the format byte directly
    if (entry.Count == 1 && entry.Type == JpegXrIfd.TYPE_BYTE)
      return entry.Value switch {
        JpegXrIfd.PIXEL_FORMAT_8BPP_GRAY => 1,
        JpegXrIfd.PIXEL_FORMAT_24BPP_RGB => 3,
        _ => 0
      };

    // For GUID-style pixel format (count > 4), the value field is an offset
    if (entry.Count > 4) {
      var offset = (int)entry.Value;
      if (offset + 1 <= data.Length)
        return data[offset] switch {
          JpegXrIfd.PIXEL_FORMAT_8BPP_GRAY => 1,
          JpegXrIfd.PIXEL_FORMAT_24BPP_RGB => 3,
          _ => 0
        };
    }

    return 0;
  }

  /// <summary>Infers component count from image byte count when pixel format tag is missing or unrecognized.</summary>
  private static int _InferComponentCount(int width, int height, uint imageByteCount) {
    var totalPixels = (uint)(width * height);
    if (totalPixels == 0)
      return 3;

    if (imageByteCount == totalPixels)
      return 1;

    return 3;
  }
}
