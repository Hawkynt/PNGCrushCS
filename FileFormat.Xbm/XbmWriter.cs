using System;
using System.Text;

namespace FileFormat.Xbm;

/// <summary>Assembles XBM file bytes from pixel data.</summary>
public static class XbmWriter {

  public static byte[] ToBytes(XbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();
    sb.AppendLine($"#define {file.Name}_width {file.Width}");
    sb.AppendLine($"#define {file.Name}_height {file.Height}");

    if (file.HotspotX.HasValue)
      sb.AppendLine($"#define {file.Name}_x_hot {file.HotspotX.Value}");

    if (file.HotspotY.HasValue)
      sb.AppendLine($"#define {file.Name}_y_hot {file.HotspotY.Value}");

    sb.AppendLine($"static unsigned char {file.Name}_bits[] = {{");

    var bytesPerRow = (file.Width + 7) / 8;
    var totalBytes = bytesPerRow * file.Height;
    var count = Math.Min(totalBytes, file.PixelData.Length);

    for (var i = 0; i < count; ++i) {
      if (i % 12 == 0)
        sb.Append("   ");

      sb.Append($"0x{file.PixelData[i]:X2}");

      if (i < count - 1)
        sb.Append(", ");

      if (i % 12 == 11 || i == count - 1)
        sb.AppendLine();
    }

    sb.AppendLine("};");

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
