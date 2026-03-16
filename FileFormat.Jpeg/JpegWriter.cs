using System;
using System.IO;
using BitMiracle.LibJpeg.Classic;

namespace FileFormat.Jpeg;

/// <summary>Encodes JPEG file bytes via lossless transcoding or lossy re-encoding.</summary>
internal static class JpegWriter {

  /// <summary>Serializes a <see cref="JpegFile"/> to bytes. Uses lossless transcode when raw bytes are available; otherwise lossy encode at quality 90.</summary>
  public static byte[] ToBytes(JpegFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.RawJpegBytes != null)
      return LosslessTranscode(file.RawJpegBytes, JpegMode.Baseline, optimizeHuffman: true, stripMetadata: false);

    if (file.RgbPixelData == null)
      throw new ArgumentException("Either RawJpegBytes or RgbPixelData must be non-null.", nameof(file));

    return LossyEncode(file.RgbPixelData, file.Width, file.Height, 90, JpegMode.Baseline, JpegSubsampling.Chroma444, optimizeHuffman: true, file.IsGrayscale);
  }

  public static byte[] LosslessTranscode(
    byte[] inputJpeg,
    JpegMode mode,
    bool optimizeHuffman,
    bool stripMetadata
  ) {
    using var inputStream = new MemoryStream(inputJpeg);
    using var outputStream = new MemoryStream();

    var srcInfo = new jpeg_decompress_struct();
    srcInfo.jpeg_stdio_src(inputStream);
    srcInfo.jpeg_read_header(true);

    var coeffArrays = srcInfo.jpeg_read_coefficients();

    var dstInfo = new jpeg_compress_struct();
    dstInfo.jpeg_stdio_dest(outputStream);

    srcInfo.jpeg_copy_critical_parameters(dstInfo);

    // Set scan mode
    if (mode == JpegMode.Progressive)
      dstInfo.jpeg_simple_progression();

    // Optimize Huffman tables
    dstInfo.Optimize_coding = optimizeHuffman;

    if (stripMetadata) {
      // Write coefficients without copying markers
      dstInfo.jpeg_write_coefficients(coeffArrays);
    } else {
      // Copy markers from source to destination
      _CopyMarkers(srcInfo, dstInfo);
      dstInfo.jpeg_write_coefficients(coeffArrays);
    }

    dstInfo.jpeg_finish_compress();
    srcInfo.jpeg_finish_decompress();

    return outputStream.ToArray();
  }

  public static byte[] LossyEncode(
    byte[] rgbPixelData,
    int width,
    int height,
    int quality,
    JpegMode mode,
    JpegSubsampling subsampling,
    bool optimizeHuffman,
    bool isGrayscale
  ) {
    using var outputStream = new MemoryStream();

    var cinfo = new jpeg_compress_struct();
    cinfo.jpeg_stdio_dest(outputStream);

    cinfo.Image_width = width;
    cinfo.Image_height = height;
    cinfo.Input_components = isGrayscale ? 1 : 3;
    cinfo.In_color_space = isGrayscale ? J_COLOR_SPACE.JCS_GRAYSCALE : J_COLOR_SPACE.JCS_RGB;

    cinfo.jpeg_set_defaults();
    cinfo.jpeg_set_quality(quality, true);

    // Set subsampling (only for color images)
    // NOTE: Chroma422 is not supported by BitMiracle.LibJpeg.NET (throws "Not implemented yet")
    if (!isGrayscale) {
      switch (subsampling) {
        case JpegSubsampling.Chroma444:
          cinfo.Component_info[0].H_samp_factor = 1;
          cinfo.Component_info[0].V_samp_factor = 1;
          cinfo.Component_info[1].H_samp_factor = 1;
          cinfo.Component_info[1].V_samp_factor = 1;
          cinfo.Component_info[2].H_samp_factor = 1;
          cinfo.Component_info[2].V_samp_factor = 1;
          break;
        case JpegSubsampling.Chroma420:
          cinfo.Component_info[0].H_samp_factor = 2;
          cinfo.Component_info[0].V_samp_factor = 2;
          cinfo.Component_info[1].H_samp_factor = 1;
          cinfo.Component_info[1].V_samp_factor = 1;
          cinfo.Component_info[2].H_samp_factor = 1;
          cinfo.Component_info[2].V_samp_factor = 1;
          break;
      }
    }

    if (mode == JpegMode.Progressive)
      cinfo.jpeg_simple_progression();

    cinfo.Optimize_coding = optimizeHuffman;

    cinfo.jpeg_start_compress(true);

    var rowStride = width * (isGrayscale ? 1 : 3);
    var rowBuffer = new byte[rowStride];

    for (var y = 0; y < height; ++y) {
      Array.Copy(rgbPixelData, y * rowStride, rowBuffer, 0, rowStride);
      var rowPointer = new byte[][] { rowBuffer };
      cinfo.jpeg_write_scanlines(rowPointer, 1);
    }

    cinfo.jpeg_finish_compress();

    return outputStream.ToArray();
  }

  private static void _CopyMarkers(jpeg_decompress_struct srcInfo, jpeg_compress_struct dstInfo) {
    // Save markers during read
    foreach (var marker in srcInfo.Marker_list) {
      if (marker.Data != null && marker.Data.Length > 0)
        dstInfo.jpeg_write_marker(marker.Marker, marker.Data);
    }
  }
}
