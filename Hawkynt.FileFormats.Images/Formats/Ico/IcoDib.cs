using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Ico;

/// <summary>
/// Builds the device-independent bitmap that an icon or cursor entry carries.
/// </summary>
/// <remarks>
/// Icons and cursors are the same file but for two fields, so the bitmap is built once here and both
/// formats reach it. What goes in an entry is not a BMP: there is no file header, the height in the
/// information header is twice the picture's, and a mask of one bit per pixel follows the colours.
/// <para/>
/// The doubled height is the part that is easy to get wrong and hard to notice, because a viewer
/// reading the entry's own width and height rather than the header's draws it correctly anyway. One
/// that trusts the header — which the Windows shell does — draws the picture squashed into the top
/// half with the mask showing through the bottom.
/// <para/>
/// The mask is kept even though every colour here carries its own alpha. A 32-bit icon does not need
/// one and Windows ignores it, but the field is not optional and tools that predate alpha read it
/// instead: leaving it out, or leaving it set, makes an icon that is invisible in the places that
/// still consult it.
/// </remarks>
internal static class IcoDib {

  /// <summary>The BITMAPINFOHEADER that opens the entry.</summary>
  private const int _InfoHeaderSize = 40;

  /// <summary>The longest side an entry can state, its width and height each being one byte.</summary>
  public const int MaximumSide = 256;

  /// <summary>That side brought within what an entry can describe.</summary>
  private static int _Fit(int side) => Math.Min(side, MaximumSide);

  /// <summary>Colours and mask alike are padded out to whole four-byte groups a row.</summary>
  private static int _Stride(int width, int bitsPerPixel) => (width * bitsPerPixel + 31) / 32 * 4;

  /// <summary>
  /// Turns a picture into one entry's worth of bytes: an information header, the colours bottom-up
  /// with alpha, and the mask.
  /// </summary>
  public static IcoImage FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new NotSupportedException($"A picture of {image.Width} by {image.Height} is no size.");

    // A directory entry states each side in one byte, with nought standing for 256, so nothing
    // larger can be described at all. A picture over that is brought down to fit rather than
    // refused: the caller asked for an icon of what it had, and the only alternative answer is none.
    var fitted = image.Width > MaximumSide || image.Height > MaximumSide
      ? image.SampleTo(_Fit(image.Width), _Fit(image.Height))
      : image;

    var width = fitted.Width;
    var height = fitted.Height;
    var bgra = fitted.ToBgra32();

    var colourStride = _Stride(width, 32);
    var maskStride = _Stride(width, 1);
    var data = new byte[_InfoHeaderSize + colourStride * height + maskStride * height];

    _WriteInfoHeader(data, width, height, colourStride * height + maskStride * height);

    // Bottom-up, which is what every bitmap of this shape means by a positive height.
    for (var y = 0; y < height; ++y) {
      var to = _InfoHeaderSize + (height - 1 - y) * colourStride;
      bgra.AsSpan(y * width * 4, width * 4).CopyTo(data.AsSpan(to));
    }

    // A set mask bit means the pixel is not drawn, so the mask is the alpha read the other way
    // round. Anything not fully transparent counts as drawn: a viewer using the mask has no way to
    // show a partly transparent pixel, and showing it is closer than dropping it.
    var maskAt = _InfoHeaderSize + colourStride * height;
    for (var y = 0; y < height; ++y) {
      var row = maskAt + (height - 1 - y) * maskStride;
      for (var x = 0; x < width; ++x)
        if (bgra[(y * width + x) * 4 + 3] == 0)
          data[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = 32,
      Format = IcoImageFormat.Bmp,
      Data = data,
    };
  }

  private static void _WriteInfoHeader(Span<byte> data, int width, int height, int imageSize) {
    BinaryPrimitives.WriteInt32LittleEndian(data, _InfoHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(data[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(data[8..], height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data[12..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data[14..], 32);
    BinaryPrimitives.WriteInt32LittleEndian(data[16..], 0);
    BinaryPrimitives.WriteInt32LittleEndian(data[20..], imageSize);
  }
}
