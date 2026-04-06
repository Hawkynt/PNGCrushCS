using System;
using FileFormat.Core;

namespace FileFormat.YuvRaw;

/// <summary>In-memory representation of a raw YUV 4:2:0 planar image.</summary>
public readonly record struct YuvRawFile : IImageFormatReader<YuvRawFile>, IImageToRawImage<YuvRawFile>, IImageFromRawImage<YuvRawFile>, IImageFormatWriter<YuvRawFile> {

  static string IImageFormatMetadata<YuvRawFile>.PrimaryExtension => ".yuv";
  static string[] IImageFormatMetadata<YuvRawFile>.FileExtensions => [".yuv"];
  static YuvRawFile IImageFormatReader<YuvRawFile>.FromSpan(ReadOnlySpan<byte> data) => YuvRawReader.FromSpan(data);
  static byte[] IImageFormatWriter<YuvRawFile>.ToBytes(YuvRawFile file) => YuvRawWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Y (luminance) plane data (Width * Height bytes).</summary>
  public byte[] YPlane { get; init; }

  /// <summary>U (Cb chrominance) plane data (Width/2 * Height/2 bytes).</summary>
  public byte[] UPlane { get; init; }

  /// <summary>V (Cr chrominance) plane data (Width/2 * Height/2 bytes).</summary>
  public byte[] VPlane { get; init; }

  /// <summary>Known YUV 4:2:0 resolutions indexed by file size (width*height*3/2).</summary>
  internal static readonly (int Width, int Height)[] KnownResolutions = [
    (176, 144),    // QCIF
    (352, 288),    // CIF
    (320, 240),    // QVGA
    (640, 480),    // VGA
    (720, 480),    // NTSC DVD
    (720, 576),    // PAL DVD
    (1280, 720),   // 720p
    (1920, 1080),  // 1080p
  ];

  /// <summary>Converts YUV 4:2:0 to Rgb24 raw image.</summary>
  public static RawImage ToRawImage(YuvRawFile file) {

    var w = file.Width;
    var h = file.Height;
    var rgb = new byte[w * h * 3];

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var yIdx = y * w + x;
        var uvIdx = (y / 2) * (w / 2) + (x / 2);

        var yVal = file.YPlane[yIdx];
        var uVal = uvIdx < file.UPlane.Length ? file.UPlane[uvIdx] : (byte)128;
        var vVal = uvIdx < file.VPlane.Length ? file.VPlane[uvIdx] : (byte)128;

        var r = yVal + 1.402 * (vVal - 128);
        var g = yVal - 0.344136 * (uVal - 128) - 0.714136 * (vVal - 128);
        var b = yVal + 1.772 * (uVal - 128);

        var offset = (y * w + x) * 3;
        rgb[offset] = _Clamp(r);
        rgb[offset + 1] = _Clamp(g);
        rgb[offset + 2] = _Clamp(b);
      }

    return new() {
      Width = w,
      Height = h,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a YUV 4:2:0 image from an Rgb24 raw image.</summary>
  public static YuvRawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var w = image.Width;
    var h = image.Height;
    var yPlane = new byte[w * h];
    var halfW = w / 2;
    var halfH = h / 2;
    var uPlane = new byte[halfW * halfH];
    var vPlane = new byte[halfW * halfH];
    var src = image.PixelData;

    // Compute Y for all pixels
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var offset = (y * w + x) * 3;
        var r = src[offset];
        var g = src[offset + 1];
        var b = src[offset + 2];
        yPlane[y * w + x] = _Clamp(0.299 * r + 0.587 * g + 0.114 * b);
      }

    // Compute U and V with 2x2 averaging
    for (var cy = 0; cy < halfH; ++cy)
      for (var cx = 0; cx < halfW; ++cx) {
        double sumU = 0, sumV = 0;
        var count = 0;
        for (var dy = 0; dy < 2; ++dy)
          for (var dx = 0; dx < 2; ++dx) {
            var px = cx * 2 + dx;
            var py = cy * 2 + dy;
            if (px >= w || py >= h)
              continue;
            var offset = (py * w + px) * 3;
            var r = src[offset];
            var g = src[offset + 1];
            var b = src[offset + 2];
            sumU += -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
            sumV += 0.5 * r - 0.418688 * g - 0.081312 * b + 128;
            ++count;
          }

        uPlane[cy * halfW + cx] = _Clamp(sumU / count);
        vPlane[cy * halfW + cx] = _Clamp(sumV / count);
      }

    return new() { Width = w, Height = h, YPlane = yPlane, UPlane = uPlane, VPlane = vPlane };
  }

  private static byte _Clamp(double value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));
}
