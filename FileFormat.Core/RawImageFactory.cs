using System;

namespace FileFormat.Core;

/// <summary>Factories for assembling canonical <see cref="RawImage"/> layouts from decoder planes.</summary>
public static class RawImageFactory {

  /// <summary>
  /// Crops three 8-bit 4:2:0 decoder planes and packs them as canonical Y, U, V planes.
  /// </summary>
  /// <remarks>
  /// Decoder reference buffers are commonly larger than the displayed picture because prediction is
  /// allowed to address coded samples outside a conformance crop. <see cref="RawImage"/> represents
  /// the picture handed to a caller, so this copies only the displayed rectangle while leaving the
  /// decoder's reference buffers untouched. A 4:2:0 crop must start on an even luma coordinate to be
  /// representable without resampling chroma; H.264/H.265 conformance windows obey that constraint.
  /// </remarks>
  public static RawImage FromYuv420P8(
    int width,
    int height,
    ReadOnlySpan<byte> yPlane,
    int yStride,
    ReadOnlySpan<byte> uPlane,
    ReadOnlySpan<byte> vPlane,
    int chromaStride,
    int left = 0,
    int top = 0,
    RawImageColorInfo? colorInfo = null,
    ImageMetadata? metadata = null) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height));
    if (left < 0 || top < 0)
      throw new ArgumentOutOfRangeException(left < 0 ? nameof(left) : nameof(top));
    if ((left & 1) != 0 || (top & 1) != 0)
      throw new ArgumentException("A 4:2:0 crop must begin on an even luma coordinate so its chroma planes remain samples rather than resampled display pixels.");
    if (yStride < left + width)
      throw new ArgumentException("The luma stride is shorter than the requested crop.", nameof(yStride));

    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;
    var chromaLeft = left >> 1;
    var chromaTop = top >> 1;
    if (chromaStride < chromaLeft + chromaWidth)
      throw new ArgumentException("The chroma stride is shorter than the requested crop.", nameof(chromaStride));

    var lastY = checked((top + height - 1) * yStride + left + width);
    var lastC = checked((chromaTop + chromaHeight - 1) * chromaStride + chromaLeft + chromaWidth);
    if (lastY > yPlane.Length)
      throw new ArgumentException("The luma plane is shorter than the requested crop.", nameof(yPlane));
    if (lastC > uPlane.Length || lastC > vPlane.Length)
      throw new ArgumentException("A chroma plane is shorter than the requested crop.", nameof(uPlane));

    var yLength = checked(width * height);
    var cLength = checked(chromaWidth * chromaHeight);
    var data = new byte[checked(yLength + 2 * cLength)];

    for (var row = 0; row < height; ++row)
      yPlane.Slice((top + row) * yStride + left, width).CopyTo(data.AsSpan(row * width, width));

    var uOffset = yLength;
    var vOffset = yLength + cLength;
    for (var row = 0; row < chromaHeight; ++row) {
      var sourceOffset = (chromaTop + row) * chromaStride + chromaLeft;
      uPlane.Slice(sourceOffset, chromaWidth).CopyTo(data.AsSpan(uOffset + row * chromaWidth, chromaWidth));
      vPlane.Slice(sourceOffset, chromaWidth).CopyTo(data.AsSpan(vOffset + row * chromaWidth, chromaWidth));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = data,
      ColorInfo = colorInfo,
      Metadata = metadata,
    };
  }
}
