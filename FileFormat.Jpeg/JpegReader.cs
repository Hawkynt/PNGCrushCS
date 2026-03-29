using System;
using System.IO;
using BitMiracle.LibJpeg.Classic;

namespace FileFormat.Jpeg;

/// <summary>Reads JPEG files from bytes, streams, or file paths.</summary>
public static class JpegReader {

  public static JpegFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegFile FromStream(Stream stream) {
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

  public static JpegFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 2)
      throw new InvalidDataException("Data too small for a valid JPEG file.");

    if (data[0] != 0xFF || data[1] != 0xD8)
      throw new InvalidDataException("Invalid JPEG signature.");

    using var inputStream = new MemoryStream(data);

    var cinfo = new jpeg_decompress_struct();
    cinfo.jpeg_stdio_src(inputStream);
    cinfo.jpeg_read_header(true);

    var isGrayscale = cinfo.Num_components == 1 ||
                      cinfo.Jpeg_color_space == J_COLOR_SPACE.JCS_GRAYSCALE;

    cinfo.Out_color_space = isGrayscale ? J_COLOR_SPACE.JCS_GRAYSCALE : J_COLOR_SPACE.JCS_RGB;

    cinfo.jpeg_start_decompress();

    var width = cinfo.Output_width;
    var height = cinfo.Output_height;
    var components = cinfo.Output_components;
    var rowStride = width * components;
    var rgbPixelData = new byte[height * width * 3];

    var rowBuffer = new byte[rowStride];
    var rowPointer = new byte[][] { rowBuffer };

    for (var y = 0; y < height; ++y) {
      cinfo.jpeg_read_scanlines(rowPointer, 1);
      if (isGrayscale)
        for (var x = 0; x < width; ++x) {
          var g = rowBuffer[x];
          var dstIdx = (y * width + x) * 3;
          rgbPixelData[dstIdx] = g;
          rgbPixelData[dstIdx + 1] = g;
          rgbPixelData[dstIdx + 2] = g;
        }
      else
        rowBuffer.AsSpan(0, width * 3).CopyTo(rgbPixelData.AsSpan(y * width * 3));
    }

    cinfo.jpeg_finish_decompress();

    return new JpegFile {
      Width = width,
      Height = height,
      IsGrayscale = isGrayscale,
      RgbPixelData = rgbPixelData,
      RawJpegBytes = data
    };
  }
}
