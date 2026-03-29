using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Nrrd;

/// <summary>Assembles NRRD file bytes from pixel data.</summary>
public static class NrrdWriter {

  public static byte[] ToBytes(NrrdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    var header = NrrdHeaderParser.Format(file);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    ms.Write(headerBytes, 0, headerBytes.Length);

    var encodedData = _EncodeData(file.PixelData, file.Encoding);
    ms.Write(encodedData, 0, encodedData.Length);

    return ms.ToArray();
  }

  internal static byte[] Assemble(byte[] pixelData, int[] sizes, NrrdType dataType, NrrdEncoding encoding, string endian, double[] spacings, string[] labels) {
    var file = new NrrdFile {
      PixelData = pixelData,
      Sizes = sizes,
      DataType = dataType,
      Encoding = encoding,
      Endian = endian,
      Spacings = spacings,
      Labels = labels
    };

    return ToBytes(file);
  }

  private static byte[] _EncodeData(byte[] pixelData, NrrdEncoding encoding) => encoding switch {
    NrrdEncoding.Raw => _CopyRaw(pixelData),
    NrrdEncoding.Gzip => _CompressGzip(pixelData),
    NrrdEncoding.Ascii => throw new NotSupportedException("ASCII encoding for NRRD writing is not supported."),
    NrrdEncoding.Hex => throw new NotSupportedException("Hex encoding for NRRD writing is not supported."),
    _ => throw new InvalidDataException($"Unsupported NRRD encoding: {encoding}.")
  };

  private static byte[] _CopyRaw(byte[] data) {
    var result = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(result);
    return result;
  }

  private static byte[] _CompressGzip(byte[] data) {
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
      gzip.Write(data, 0, data.Length);

    return output.ToArray();
  }
}
