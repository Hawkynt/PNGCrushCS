using System;
using System.Text;

namespace FileFormat.CsvImage;

/// <summary>Assembles CSV image file bytes from a CsvImageFile.</summary>
public static class CsvImageWriter {

  public static byte[] ToBytes(CsvImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();
    sb.Append(file.Width);
    sb.Append(',');
    sb.Append(file.Height);
    sb.Append('\n');

    for (var y = 0; y < file.Height; ++y) {
      for (var x = 0; x < file.Width; ++x) {
        if (x > 0)
          sb.Append(',');

        var index = y * file.Width + x;
        sb.Append(index < file.PixelData.Length ? file.PixelData[index] : 0);
      }

      sb.Append('\n');
    }

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
