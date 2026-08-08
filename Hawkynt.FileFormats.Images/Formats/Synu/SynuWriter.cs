using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Synu;

/// <summary>Assembles a Synu picture: the five header lines, then the samples bottom up.</summary>
public static class SynuWriter {

  public static byte[] ToBytes(SynuFile file) {
    var channels = file.Channels is 1 or 3 ? file.Channels : 3;
    var stride = file.Width * channels;
    var bytes = stride * file.Height;

    var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
      $"image 4L {bytes}b\n{file.Width}\n{file.Height}\n{channels}\n{(string.IsNullOrEmpty(file.ColorSpace) ? (channels == 1 ? "bw" : "rgb") : file.ColorSpace)}\n"));

    var pixels = file.PixelData ?? [];
    var result = new byte[header.Length + bytes];
    header.CopyTo(result, 0);

    for (var y = 0; y < file.Height; ++y) {
      var from = y * stride;
      if (from + stride > pixels.Length)
        break;

      pixels.AsSpan(from, stride).CopyTo(result.AsSpan(header.Length + (file.Height - 1 - y) * stride));
    }

    return result;
  }
}
