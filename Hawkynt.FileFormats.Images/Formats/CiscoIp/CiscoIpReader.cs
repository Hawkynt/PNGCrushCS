using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FileFormat.Core;

namespace FileFormat.CiscoIp;

/// <summary>Reads Cisco IP Phone image documents from bytes, streams, or file paths.</summary>
public static class CiscoIpReader {

  public static CiscoIpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CiscoIpFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static CiscoIpFile FromSpan(ReadOnlySpan<byte> data) {
    var text = Encoding.ASCII.GetString(data);
    if (!text.Contains(CiscoIpFile.RootElement, StringComparison.Ordinal))
      throw new InvalidDataException("Not a Cisco IP Phone image.");

    var width = _Number(text, "Width");
    var height = _Number(text, "Height");
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Cisco IP Phone image states no size: {width}x{height}.");

    var hex = Regex.Match(text, "<Data>(.*?)</Data>", RegexOptions.Singleline).Groups[1].Value;
    var packed = _FromHex(hex);

    var stride = (width * CiscoIpFile.BitsPerPixel + 7) / 8;
    if (packed.Length < stride * height)
      throw new InvalidDataException(
        $"{width}x{height} needs {stride * height} bytes; the data holds {packed.Length}.");

    return new() {
      Width = width,
      Height = height,
      Title = Regex.Match(text, "<Title>(.*?)</Title>", RegexOptions.Singleline).Groups[1].Value,
      LocationX = _Number(text, "LocationX"),
      LocationY = _Number(text, "LocationY"),
      PixelData = PackedRows.Unpack(packed, width, height, CiscoIpFile.BitsPerPixel, stride),
    };
  }

  private static int _Number(string text, string element) {
    var match = Regex.Match(text, $"<{element}>\\s*(-?\\d+)\\s*</{element}>");
    return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
  }

  /// <summary>Turns the document's hexadecimal body back into bytes, ignoring any layout in it.</summary>
  private static byte[] _FromHex(string hex) {
    var digits = new StringBuilder(hex.Length);
    foreach (var c in hex)
      if (Uri.IsHexDigit(c))
        digits.Append(c);

    var bytes = new byte[digits.Length / 2];
    for (var i = 0; i < bytes.Length; ++i)
      bytes[i] = (byte)((Uri.FromHex(digits[i * 2]) << 4) | Uri.FromHex(digits[i * 2 + 1]));

    return bytes;
  }

  public static CiscoIpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
