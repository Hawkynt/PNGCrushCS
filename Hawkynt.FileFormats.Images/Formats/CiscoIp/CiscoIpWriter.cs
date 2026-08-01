using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CiscoIp;

/// <summary>Assembles Cisco IP Phone image documents.</summary>
public static class CiscoIpWriter {

  public static byte[] ToBytes(CiscoIpFile file) {
    var packed = PackedRows.Pack(
      file.PixelData ?? [], file.Width, file.Height, CiscoIpFile.BitsPerPixel, file.Stride);

    var builder = new StringBuilder();
    builder.Append('<').Append(CiscoIpFile.RootElement).Append(">\n");
    builder.Append("<Title>").Append(_Escaped(file.Title)).Append("</Title>\n");
    builder.Append("<LocationX>").Append(file.LocationX).Append("</LocationX>\n");
    builder.Append("<LocationY>").Append(file.LocationY).Append("</LocationY>\n");
    builder.Append("<Width>").Append(file.Width).Append("</Width>\n");
    builder.Append("<Height>").Append(file.Height).Append("</Height>\n");
    builder.Append("<Depth>").Append(CiscoIpFile.BitsPerPixel).Append("</Depth>\n");

    builder.Append("<Data>");
    foreach (var b in packed)
      builder.Append(b.ToString("x2"));

    builder.Append("</Data>\n");
    builder.Append("</").Append(CiscoIpFile.RootElement).Append(">\n");

    return Encoding.ASCII.GetBytes(builder.ToString());
  }

  /// <summary>Keeps a title from closing the element it sits in.</summary>
  private static string _Escaped(string? title) => (title ?? string.Empty)
    .Replace("&", "&amp;")
    .Replace("<", "&lt;")
    .Replace(">", "&gt;");
}
