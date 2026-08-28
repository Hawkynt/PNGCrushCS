using System;
using System.Buffers.Binary;
using SharpAstro.Jxr;

namespace FileFormat.JpegXr;

/// <summary>Writes a T.833 JPEG XR container around standards-valid T.832 codestreams.</summary>
public static class JpegXrWriter {

  private const int _IFD_OFFSET = 8;

  public static byte[] ToBytes(JpegXrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.ComponentCount);
  }

  internal static byte[] _Assemble(byte[] pixelData, int width, int height, int componentCount) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "JPEG XR dimensions must be positive.");
    if (componentCount is not (1 or 3 or 4))
      throw new NotSupportedException($"JPEG XR writer supports Gray8, RGB24, and RGBA32; got {componentCount} components.");

    var pixelCount = checked(width * height);
    var expectedLength = checked(pixelCount * componentCount);
    if (pixelData.Length != expectedLength)
      throw new ArgumentException($"JPEG XR pixel buffer has {pixelData.Length} bytes; expected {expectedLength} for {width}x{height}x{componentCount}.", nameof(pixelData));

    byte[] imageCodestream;
    byte[]? alphaCodestream = null;
    if (componentCount == 1) {
      var y = new int[pixelCount];
      for (var i = 0; i < pixelCount; ++i)
        y[i] = pixelData[i];
      imageCodestream = JxrCodestream.EncodeGray(y, width, height, overlap: 0);
    } else {
      var r = new int[pixelCount];
      var g = new int[pixelCount];
      var b = new int[pixelCount];
      int[]? a = componentCount == 4 ? new int[pixelCount] : null;
      for (var i = 0; i < pixelCount; ++i) {
        var source = i * componentCount;
        r[i] = pixelData[source];
        g[i] = pixelData[source + 1];
        b[i] = pixelData[source + 2];
        if (a is not null)
          a[i] = pixelData[source + 3];
      }
      imageCodestream = JxrCodestream.Encode(r, g, b, width, height, overlap: 0);
      if (a is not null)
        alphaCodestream = JxrCodestream.EncodeGray(a, width, height, overlap: 0);
    }

    var pixelFormatGuid = JpegXrIfd.CreatePixelFormatGuid(componentCount);
    var ifdEntryCount = alphaCodestream is null ? 5 : 7;
    var ifdSize = 2 + ifdEntryCount * 12 + 4;
    var guidOffset = _Align4(_IFD_OFFSET + ifdSize);
    var imageOffset = _Align4(guidOffset + pixelFormatGuid.Length);
    var alphaOffset = alphaCodestream is null ? 0 : _Align4(checked(imageOffset + imageCodestream.Length));
    var fileSize = alphaCodestream is null
      ? checked(imageOffset + imageCodestream.Length)
      : checked(alphaOffset + alphaCodestream.Length);

    var result = new byte[fileSize];
    var span = result.AsSpan();
    result[0] = (byte)'I';
    result[1] = (byte)'I';
    new JpegXrHeader(JpegXrReader.JPEGXR_MAGIC, _IFD_OFFSET).WriteTo(span);

    var pos = _IFD_OFFSET;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)ifdEntryCount);
    pos += 2;

    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_PIXEL_FORMAT, JpegXrIfd.TYPE_BYTE, 16, (uint)guidOffset);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_WIDTH, JpegXrIfd.TYPE_LONG, 1, (uint)width);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_HEIGHT, JpegXrIfd.TYPE_LONG, 1, (uint)height);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_OFFSET, JpegXrIfd.TYPE_LONG, 1, (uint)imageOffset);
    JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_IMAGE_BYTE_COUNT, JpegXrIfd.TYPE_LONG, 1, (uint)imageCodestream.Length);
    if (alphaCodestream is not null) {
      JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_ALPHA_OFFSET, JpegXrIfd.TYPE_LONG, 1, (uint)alphaOffset);
      JpegXrIfd.WriteEntry(span, ref pos, JpegXrIfd.TAG_ALPHA_BYTE_COUNT, JpegXrIfd.TYPE_LONG, 1, (uint)alphaCodestream.Length);
    }
    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    pixelFormatGuid.CopyTo(result, guidOffset);
    imageCodestream.CopyTo(result, imageOffset);
    alphaCodestream?.CopyTo(result, alphaOffset);
    return result;
  }

  private static int _Align4(int value) => checked((value + 3) & ~3);
}
