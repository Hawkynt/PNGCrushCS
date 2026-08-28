using System;
using System.Buffers.Binary;
using System.Globalization;
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

    var encodedData = _EncodeData(file.PixelData, file.DataType, file.Encoding, file.Endian);
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

  private static byte[] _EncodeData(byte[] pixelData, NrrdType dataType, NrrdEncoding encoding, string endian) => encoding switch {
    NrrdEncoding.Raw => _CopyRaw(pixelData),
    NrrdEncoding.Gzip => _CompressGzip(pixelData),
    NrrdEncoding.Ascii => _EncodeAscii(pixelData, dataType, endian),
    NrrdEncoding.Hex => _EncodeHex(pixelData),
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

  private static byte[] _EncodeAscii(byte[] data, NrrdType dataType, string endian) {
    var bytesPerElement = _BytesPerElement(dataType);
    if (data.Length % bytesPerElement != 0)
      throw new InvalidDataException(
        $"NRRD {dataType} payload has {data.Length} bytes, which is not a whole number of {bytesPerElement}-byte samples.");

    var isBigEndian = string.Equals(endian, "big", StringComparison.OrdinalIgnoreCase);
    if (!isBigEndian && !string.Equals(endian, "little", StringComparison.OrdinalIgnoreCase) && bytesPerElement > 1)
      throw new InvalidDataException($"NRRD endian must be 'little' or 'big', got '{endian}'.");

    var count = data.Length / bytesPerElement;
    var text = new StringBuilder(Math.Max(16, count * 4));
    for (var i = 0; i < count; ++i) {
      if (i != 0)
        text.Append(i % 16 == 0 ? '\n' : ' ');

      text.Append(_FormatAsciiElement(data.AsSpan(i * bytesPerElement, bytesPerElement), dataType, isBigEndian));
    }

    if (count > 0)
      text.Append('\n');

    return Encoding.ASCII.GetBytes(text.ToString());
  }

  private static string _FormatAsciiElement(ReadOnlySpan<byte> sample, NrrdType dataType, bool isBigEndian) => dataType switch {
    NrrdType.Int8 => ((sbyte)sample[0]).ToString(CultureInfo.InvariantCulture),
    NrrdType.UInt8 => sample[0].ToString(CultureInfo.InvariantCulture),
    NrrdType.Int16 => (isBigEndian
      ? BinaryPrimitives.ReadInt16BigEndian(sample)
      : BinaryPrimitives.ReadInt16LittleEndian(sample)).ToString(CultureInfo.InvariantCulture),
    NrrdType.UInt16 => (isBigEndian
      ? BinaryPrimitives.ReadUInt16BigEndian(sample)
      : BinaryPrimitives.ReadUInt16LittleEndian(sample)).ToString(CultureInfo.InvariantCulture),
    NrrdType.Int32 => (isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(sample)
      : BinaryPrimitives.ReadInt32LittleEndian(sample)).ToString(CultureInfo.InvariantCulture),
    NrrdType.UInt32 => (isBigEndian
      ? BinaryPrimitives.ReadUInt32BigEndian(sample)
      : BinaryPrimitives.ReadUInt32LittleEndian(sample)).ToString(CultureInfo.InvariantCulture),
    NrrdType.Float => BitConverter.Int32BitsToSingle(isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(sample)
      : BinaryPrimitives.ReadInt32LittleEndian(sample)).ToString("R", CultureInfo.InvariantCulture),
    NrrdType.Double => BitConverter.Int64BitsToDouble(isBigEndian
      ? BinaryPrimitives.ReadInt64BigEndian(sample)
      : BinaryPrimitives.ReadInt64LittleEndian(sample)).ToString("R", CultureInfo.InvariantCulture),
    _ => throw new InvalidDataException($"Unsupported NRRD data type: {dataType}.")
  };

  private static byte[] _EncodeHex(byte[] data) {
    if (data.Length == 0)
      return [];

    const string digits = "0123456789abcdef";
    const int bytesPerLine = 32;
    var lineBreaks = (data.Length - 1) / bytesPerLine + 1;
    var result = new byte[data.Length * 2 + lineBreaks];
    var at = 0;

    for (var i = 0; i < data.Length; ++i) {
      var value = data[i];
      result[at++] = (byte)digits[value >> 4];
      result[at++] = (byte)digits[value & 0x0F];
      if ((i + 1) % bytesPerLine == 0 || i + 1 == data.Length)
        result[at++] = (byte)'\n';
    }

    return result;
  }

  private static int _BytesPerElement(NrrdType dataType) => dataType switch {
    NrrdType.Int8 or NrrdType.UInt8 => 1,
    NrrdType.Int16 or NrrdType.UInt16 => 2,
    NrrdType.Int32 or NrrdType.UInt32 or NrrdType.Float => 4,
    NrrdType.Double => 8,
    _ => throw new InvalidDataException($"Unsupported NRRD data type: {dataType}.")
  };
}
