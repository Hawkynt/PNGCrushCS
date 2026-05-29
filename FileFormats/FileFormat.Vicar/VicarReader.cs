using System;
using System.IO;
using System.Text;

namespace FileFormat.Vicar;

/// <summary>Reads NASA JPL VICAR files from bytes, streams, or file paths.</summary>
public static class VicarReader {

  private const string _MAGIC = "LBLSIZE=";
  private const int _MIN_HEADER_SIZE = 16;

  public static VicarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VICAR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VicarFile FromStream(Stream stream) {
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

  public static VicarFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid VICAR file.");

    var magic = Encoding.ASCII.GetString(data.Slice(0, _MAGIC.Length));
    if (!magic.Equals(_MAGIC, StringComparison.Ordinal))
      throw new InvalidDataException("Invalid VICAR signature: header must start with 'LBLSIZE='.");

    var lblSizeRel = data[_MAGIC.Length..].IndexOf((byte)' ');
    var lblSizeEnd = lblSizeRel >= 0 ? lblSizeRel + _MAGIC.Length : -1;
    if (lblSizeEnd < 0) {
      for (lblSizeEnd = _MAGIC.Length; lblSizeEnd < data.Length; ++lblSizeEnd)
        if (data[lblSizeEnd] < '0' || data[lblSizeEnd] > '9')
          break;
    }

    var lblSizeText = Encoding.ASCII.GetString(data.Slice(_MAGIC.Length, lblSizeEnd - _MAGIC.Length));
    if (!int.TryParse(lblSizeText, out var lblSize) || lblSize <= 0)
      throw new InvalidDataException($"Invalid LBLSIZE value: '{lblSizeText}'.");

    if (data.Length < lblSize)
      throw new InvalidDataException($"Data too small for declared LBLSIZE={lblSize}.");

    var headerText = Encoding.ASCII.GetString(data.Slice(0, lblSize));
    var labels = VicarHeaderParser.Parse(headerText);

    var nl = _GetIntLabel(labels, "NL");
    var ns = _GetIntLabel(labels, "NS");
    var nb = labels.ContainsKey("NB") ? _GetIntLabel(labels, "NB") : 1;

    var format = _GetStringLabel(labels, "FORMAT", "BYTE");
    var org = _GetStringLabel(labels, "ORG", "BSQ");
    var intfmt = _GetStringLabel(labels, "INTFMT", "LOW");
    var realfmt = _GetStringLabel(labels, "REALFMT", "IEEE");

    var pixelType = _ParsePixelType(format);
    var organization = _ParseOrganization(org);
    var bytesPerPixel = _BytesPerPixel(pixelType);

    var expectedPixelBytes = ns * nl * nb * bytesPerPixel;
    var available = data.Length - lblSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    if (copyLen > 0)
      data.Slice(lblSize, copyLen).CopyTo(pixelData);

    return new VicarFile {
      Width = ns,
      Height = nl,
      Bands = nb,
      PixelType = pixelType,
      Organization = organization,
      IntFormat = intfmt,
      RealFormat = realfmt,
      PixelData = pixelData,
      Labels = labels
    };
  
  }

  public static VicarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static int _GetIntLabel(System.Collections.Generic.Dictionary<string, string> labels, string key) {
    if (!labels.TryGetValue(key, out var val) || !int.TryParse(val, out var result))
      throw new InvalidDataException($"Missing or invalid VICAR label: {key}.");

    return result;
  }

  private static string _GetStringLabel(System.Collections.Generic.Dictionary<string, string> labels, string key, string defaultValue)
    => labels.TryGetValue(key, out var val) ? val : defaultValue;

  private static VicarPixelType _ParsePixelType(string format) => format.ToUpperInvariant() switch {
    "BYTE" => VicarPixelType.Byte,
    "HALF" => VicarPixelType.Half,
    "FULL" => VicarPixelType.Full,
    "REAL" => VicarPixelType.Real,
    "DOUB" => VicarPixelType.Doub,
    _ => throw new InvalidDataException($"Unsupported VICAR FORMAT: '{format}'.")
  };

  private static VicarOrganization _ParseOrganization(string org) => org.ToUpperInvariant() switch {
    "BSQ" => VicarOrganization.Bsq,
    "BIL" => VicarOrganization.Bil,
    "BIP" => VicarOrganization.Bip,
    _ => throw new InvalidDataException($"Unsupported VICAR ORG: '{org}'.")
  };

  internal static int _BytesPerPixel(VicarPixelType pixelType) => pixelType switch {
    VicarPixelType.Byte => 1,
    VicarPixelType.Half => 2,
    VicarPixelType.Full => 4,
    VicarPixelType.Real => 4,
    VicarPixelType.Doub => 8,
    _ => throw new ArgumentOutOfRangeException(nameof(pixelType))
  };
}
