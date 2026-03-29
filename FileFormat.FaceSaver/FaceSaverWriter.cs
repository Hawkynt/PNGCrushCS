using System;
using System.Text;

namespace FileFormat.FaceSaver;

/// <summary>Writes Usenix FaceSaver files.</summary>
public static class FaceSaverWriter {

  private const int _HEX_PAIRS_PER_LINE = 30;

  public static byte[] ToBytes(FaceSaverFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();

    // Write header fields
    sb.Append("FirstName: ").Append(file.FirstName).Append('\n');
    sb.Append("LastName: ").Append(file.LastName).Append('\n');
    sb.Append("E-mail: ").Append(file.Email).Append('\n');
    sb.Append("Telephone: ").Append(file.Telephone).Append('\n');
    sb.Append("Company: ").Append(file.Company).Append('\n');
    sb.Append("Address1: ").Append(file.Address1).Append('\n');
    sb.Append("Address2: ").Append(file.Address2).Append('\n');
    sb.Append("CityStateZip: ").Append(file.CityStateZip).Append('\n');
    sb.Append("Date: ").Append(file.Date).Append('\n');
    sb.Append("PicData: ").Append(file.Width).Append(' ').Append(file.Height).Append(' ').Append(file.BitsPerPixel).Append('\n');

    var imgW = file.ImageWidth > 0 ? file.ImageWidth : file.Width;
    var imgH = file.ImageHeight > 0 ? file.ImageHeight : file.Height;
    sb.Append("Image: ").Append(imgW).Append(' ').Append(imgH).Append(' ').Append(file.BitsPerPixel).Append('\n');

    // Blank line separator
    sb.Append('\n');

    // Write hex-encoded pixel data (bottom-to-top, pixel data stored top-to-bottom)
    var cols = file.Width;
    var rows = file.Height;
    var itemsOnLine = 0;

    for (var fileRow = 0; fileRow < rows; ++fileRow) {
      var srcRow = rows - 1 - fileRow;
      for (var col = 0; col < cols; ++col) {
        if (itemsOnLine == _HEX_PAIRS_PER_LINE) {
          sb.Append('\n');
          itemsOnLine = 0;
        }

        var pixel = file.PixelData[srcRow * cols + col];
        sb.Append("0123456789abcdef"[pixel >> 4]);
        sb.Append("0123456789abcdef"[pixel & 0xF]);
        ++itemsOnLine;
      }
    }

    sb.Append('\n');

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
