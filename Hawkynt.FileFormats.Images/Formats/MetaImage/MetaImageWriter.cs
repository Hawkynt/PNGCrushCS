using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.MetaImage;

/// <summary>Assembles MetaImage (.mha) file bytes from pixel data.</summary>
public static class MetaImageWriter {

  public static byte[] ToBytes(MetaImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(MetaImageFile file) {
    using var ms = new MemoryStream();

    var header = _BuildHeader(file);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    ms.Write(headerBytes);

    if (file.IsCompressed) {
      var compressed = _CompressGzip(file.PixelData);
      ms.Write(compressed);
    } else
      ms.Write(file.PixelData);

    return ms.ToArray();
  }

  private static string _BuildHeader(MetaImageFile file) {
    var sb = new StringBuilder();
    sb.AppendLine("ObjectType = Image");
    sb.AppendLine("NDims = 2");
    sb.Append("DimSize = ").Append(file.Width).Append(' ').AppendLine(file.Height.ToString());
    sb.Append("ElementType = ").AppendLine(_FormatElementType(file.ElementType));

    if (file.Channels != 1)
      sb.Append("ElementNumberOfChannels = ").AppendLine(file.Channels.ToString());

    if (file.IsCompressed)
      sb.AppendLine("CompressedData = True");

    sb.AppendLine("ElementDataFile = LOCAL");
    return sb.ToString();
  }

  private static string _FormatElementType(MetaImageElementType type) => type switch {
    MetaImageElementType.MetUChar => "MET_UCHAR",
    MetaImageElementType.MetShort => "MET_SHORT",
    MetaImageElementType.MetUShort => "MET_USHORT",
    MetaImageElementType.MetFloat => "MET_FLOAT",
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
  };

  private static byte[] _CompressGzip(byte[] data) {
    using var outputStream = new MemoryStream();
    using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
      gzipStream.Write(data);

    return outputStream.ToArray();
  }
}
