using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>A decoded JPEG XR frame in the file's native pixel format (rows packed, little-endian samples).</summary>
internal sealed record JxrDecodedImage(Guid PixelFormat, string PixelFormatName, int Width, int Height, int BitsPerPixel, int Stride, byte[] Pixels);

/// <summary>
/// Managed JPEG XR decoder that reproduces Microsoft's jxrlib bit for bit: container parsing as in
/// JXRGlueJxr.c, the T.832 core as in image/decode, and PKImageDecode_Copy_WMP's row-by-row output into the
/// native pixel format (planar alpha decoded as a second codestream into the alpha samples).
/// </summary>
internal static class JxrLibDecoder {

  internal const int MaxDimension = 1 << 24;
  private const long _MAX_OUTPUT_BYTES = 1L << 30;

  private const ushort _TAG_PIXEL_FORMAT = 0xbc01;
  private const ushort _TAG_TRANSFORMATION = 0xbc02;
  private const ushort _TAG_IMAGE_WIDTH = 0xbc80;
  private const ushort _TAG_IMAGE_HEIGHT = 0xbc81;
  private const ushort _TAG_WIDTH_RESOLUTION = 0xbc82;
  private const ushort _TAG_HEIGHT_RESOLUTION = 0xbc83;
  private const ushort _TAG_IMAGE_OFFSET = 0xbcc0;
  private const ushort _TAG_IMAGE_BYTE_COUNT = 0xbcc1;
  private const ushort _TAG_ALPHA_OFFSET = 0xbcc2;
  private const ushort _TAG_ALPHA_BYTE_COUNT = 0xbcc3;

  private sealed class Container {
    internal JxrPixelFormatInfo? PixelFormat;
    internal int Orientation;
    internal bool OrientationFromContainer;
    internal uint ImageOffset;
    internal uint AlphaOffset;
    internal uint AlphaByteCount;
  }

  /// <summary>Decodes a whole .jxr file (TIFF-like container "II\xBC\x01") into its native pixel format.</summary>
  internal static JxrDecodedImage Decode(ReadOnlySpan<byte> file) {
    var data = file.ToArray();
    try {
      return _Decode(data);
    } catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or OverflowException or NullReferenceException) {
      throw new InvalidDataException("JPEG XR: corrupt codestream.", e);
    }
  }

  private static ushort _U16(byte[] d, long pos) {
    if (pos < 0 || pos + 2 > d.Length)
      throw new InvalidDataException("JPEG XR: container field outside the file.");
    return BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan((int)pos));
  }

  private static uint _U32(byte[] d, long pos) {
    if (pos < 0 || pos + 4 > d.Length)
      throw new InvalidDataException("JPEG XR: container field outside the file.");
    return BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)pos));
  }

  private static Container _ReadContainer(byte[] d) {
    if (d.Length < 8 || d[0] != (byte)'I' || d[1] != (byte)'I')
      throw new InvalidDataException("JPEG XR: not a little-endian container.");
    var id = _U16(d, 2);
    if ((id & 0xff) != 0xbc)
      throw new InvalidDataException("JPEG XR: not a JPEG XR container.");
    if ((id >> 8) is not (0 or 1))
      throw new InvalidDataException("JPEG XR: unsupported container version.");

    long pos = _U32(d, 4);
    var entries = _U16(d, pos);
    if (entries is 0 or ushort.MaxValue)
      throw new InvalidDataException("JPEG XR: empty image directory.");
    pos += 2;

    var c = new Container();
    for (var i = 0; i < entries; ++i, pos += 12) {
      var tag = _U16(d, pos);
      var count = _U32(d, pos + 4);
      var value = _U32(d, pos + 8);
      switch (tag) {
        case _TAG_PIXEL_FORMAT: {
          // T.833 states the pixel format as a 16-byte GUID; anything shorter is not that field.
          if (count != 16)
            throw new InvalidDataException($"JPEG XR: the pixel format is {count} bytes where T.833 states a 16-byte GUID.");
          if ((long)value + 16 > d.Length)
            throw new InvalidDataException("JPEG XR: pixel format outside the file.");
          var guid = new Guid(d.AsSpan((int)value, 16));
          c.PixelFormat = JxrPixelFormats.Find(guid) ?? throw new NotSupportedException($"JPEG XR: unsupported pixel format {guid}.");
          break;
        }
        case _TAG_TRANSFORMATION:
          if (count != 1 || value >= 8)
            throw new InvalidDataException("JPEG XR: invalid orientation.");
          c.OrientationFromContainer = true;
          c.Orientation = (int)value;
          break;
        case _TAG_IMAGE_WIDTH:
        case _TAG_IMAGE_HEIGHT:
          if (value == 0)
            throw new InvalidDataException("JPEG XR: zero image dimension.");
          break;
        case _TAG_IMAGE_OFFSET:
          if (count != 1)
            throw new InvalidDataException("JPEG XR: invalid image offset.");
          c.ImageOffset = value;
          break;
        case _TAG_IMAGE_BYTE_COUNT:
        case _TAG_WIDTH_RESOLUTION:
        case _TAG_HEIGHT_RESOLUTION:
          if (count != 1)
            throw new InvalidDataException("JPEG XR: invalid directory entry.");
          break;
        case _TAG_ALPHA_OFFSET:
          if (count != 1)
            throw new InvalidDataException("JPEG XR: invalid alpha offset.");
          c.AlphaOffset = value;
          break;
        case _TAG_ALPHA_BYTE_COUNT:
          if (count != 1)
            throw new InvalidDataException("JPEG XR: invalid alpha byte count.");
          c.AlphaByteCount = value;
          break;
      }
    }

    if (c.PixelFormat == null)
      throw new InvalidDataException("JPEG XR: missing pixel format.");
    return c;
  }

  private static JxrDecodedImage _Decode(byte[] data) {
    var c = _ReadContainer(data);
    var pf = c.PixelFormat!;
    var hasAlpha = pf.HasAlpha && c.AlphaOffset != 0 && c.AlphaByteCount != 0;

    // PKImageDecode_Initialize_WMP: header info, container orientation overrides the codestream's
    var ii = new JxrImageInfo { BitsPerUnit = pf.BitsPerUnit, Rgb = !pf.Bgr };
    var scp = new JxrCodecParams();
    JxrStrCodec.ReadWmiHeader(data, c.ImageOffset, ii, scp, new JxrCoreParams());
    ii.Orientation = c.OrientationFromContainer ? c.Orientation : 0;
    var width = ii.Width;
    var height = ii.Height;
    if (ii.Orientation >= 4)
      throw new NotSupportedException("JPEG XR: rotated orientations are not decoded by jxrlib's row-based copy.");

    var stride = ((long)width * pf.BitsPerUnit + 7) / 8;
    if (stride * height > _MAX_OUTPUT_BYTES)
      throw new InvalidDataException("JPEG XR: image too large.");
    var output = new byte[stride * height + 64];

    // Alpha is asked for whenever the pixel format has it, as JxrDecApp asks: planar alpha comes from its own
    // plane below, interleaved alpha from the main codestream's secondary channel.
    scp.AlphaMode = pf.HasAlpha ? 2 : 0;

    // PKImageDecode_Copy_WMP, re-entrant mode, one macroblock row per call
    const int linesPerMBRow = 16;
    var linesCropped = 0;
    var savedAlphaMode = scp.AlphaMode;
    if (hasAlpha)
      scp.AlphaMode = 0;

    var codec = JxrStrCodec.Create(data, c.ImageOffset, ii, scp);
    var mbRows = _MBRowCount(linesCropped, height, ii.RoiHeight, linesPerMBRow);
    var firstNonZero = false;
    for (var i = 0; i < mbRows && i <= codec.MbHeight; ++i) {
      var lines = codec.DecodeRow(i, output, height, (int)stride);
      if (!firstNonZero && lines > 0) {
        linesCropped += linesPerMBRow - lines;
        firstNonZero = true;
        mbRows = _MBRowCount(linesCropped, height, ii.RoiHeight, linesPerMBRow);
      }

      if (lines == 0 && i > 0) {
        linesCropped += linesPerMBRow;
        ++mbRows;
      }
    }

    if (hasAlpha)
      scp.AlphaMode = savedAlphaMode;

    if (hasAlpha && scp.AlphaMode != 0) {
      var iiAlpha = ii.Clone();
      var scpAlpha = scp.Clone();
      iiAlpha.ColorFormat = JxrColorFormat.YOnly;
      iiAlpha.LeadingPadding += ii.BitDepth switch {
        JxrBitDepth.Bd8 => (ii.BitsPerUnit >> 3) - 1,
        JxrBitDepth.Bd16 or JxrBitDepth.Bd16S or JxrBitDepth.Bd16F => (ii.BitsPerUnit >> 3) / 2 - 1,
        JxrBitDepth.Bd32 or JxrBitDepth.Bd32S or JxrBitDepth.Bd32F => (ii.BitsPerUnit >> 3) / 4 - 1,
        _ => 0,
      };

      var alpha = JxrStrCodec.Create(data, c.AlphaOffset, iiAlpha, scpAlpha);
      mbRows = _MBRowCount(linesCropped, height, ii.RoiHeight, linesPerMBRow);
      for (var i = 0; i < mbRows && i <= alpha.MbHeight; ++i)
        alpha.DecodeRow(i, output, height, (int)stride);
    }

    var pixels = new byte[stride * height];
    Array.Copy(output, pixels, pixels.Length);
    return new JxrDecodedImage(pf.Guid, pf.Name, width, height, pf.BitsPerUnit, (int)stride, pixels);
  }

  /// <summary>Rows to request so the last partial macroblock row is flushed; the decoder lags one row behind.</summary>
  private static int _MBRowCount(int linesCropped, int requestHeight, int roiHeight, int linesPerMBRow)
    => (linesCropped + requestHeight + (requestHeight >= roiHeight ? linesPerMBRow - 1 : 0)) / linesPerMBRow + 1;
}
