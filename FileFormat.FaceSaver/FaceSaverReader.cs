using System;
using System.IO;
using System.Text;

namespace FileFormat.FaceSaver;

/// <summary>Reads Usenix FaceSaver files.</summary>
public static class FaceSaverReader {

  public static FaceSaverFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FaceSaverFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static FaceSaverFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 10)
      throw new InvalidDataException("Data too small for FaceSaver file.");

    var text = Encoding.ASCII.GetString(data);
    return _Parse(text);
  }

  private static FaceSaverFile _Parse(string text) {
    var lines = text.Split('\n');

    var firstName = string.Empty;
    var lastName = string.Empty;
    var email = string.Empty;
    var telephone = string.Empty;
    var company = string.Empty;
    var address1 = string.Empty;
    var address2 = string.Empty;
    var cityStateZip = string.Empty;
    var date = string.Empty;
    var cols = 0;
    var rows = 0;
    var depth = 0;
    var xcols = 0;
    var xrows = 0;
    var headerEnd = -1;

    for (var i = 0; i < lines.Length; ++i) {
      var line = lines[i].TrimEnd('\r');

      // Blank line ends header
      if (line.Length == 0) {
        headerEnd = i;
        break;
      }

      var colonIdx = line.IndexOf(':');
      if (colonIdx <= 0)
        continue;

      var key = line[..colonIdx].Trim();
      var value = line[(colonIdx + 1)..].Trim();

      switch (key) {
        case "FirstName":
          firstName = value;
          break;
        case "LastName":
          lastName = value;
          break;
        case "E-mail":
          email = value;
          break;
        case "Telephone":
          telephone = value;
          break;
        case "Company":
          company = value;
          break;
        case "Address1":
          address1 = value;
          break;
        case "Address2":
          address2 = value;
          break;
        case "CityStateZip":
          cityStateZip = value;
          break;
        case "Date":
          date = value;
          break;
        case "PicData":
          _ParseDimensions(value, out cols, out rows, out depth);
          break;
        case "Image":
          _ParseDimensions(value, out xcols, out xrows, out _);
          break;
      }
    }

    if (cols <= 0 || rows <= 0)
      throw new InvalidDataException("Missing or invalid PicData header field.");

    if (depth != 8)
      throw new InvalidDataException($"Only 8 bits per pixel is supported, got {depth}.");

    // Read hex-encoded pixel data after header (bottom-to-top in file, stored top-to-bottom)
    var pixelData = new byte[cols * rows];
    var pixelIndex = 0;
    var highNibble = true;
    byte currentByte = 0;

    for (var i = headerEnd + 1; i < lines.Length && pixelIndex < pixelData.Length; ++i) {
      var line = lines[i].TrimEnd('\r');
      foreach (var c in line) {
        if (!_IsHexDigit(c))
          continue;

        if (highNibble) {
          currentByte = (byte)(_HexValue(c) << 4);
          highNibble = false;
        } else {
          currentByte |= (byte)_HexValue(c);
          var fileRow = pixelIndex / cols;
          var col = pixelIndex % cols;
          var destRow = rows - 1 - fileRow;
          pixelData[destRow * cols + col] = currentByte;
          ++pixelIndex;
          highNibble = true;
        }
      }
    }

    return new() {
      Width = cols,
      Height = rows,
      BitsPerPixel = depth,
      ImageWidth = xcols,
      ImageHeight = xrows,
      FirstName = firstName,
      LastName = lastName,
      Email = email,
      Telephone = telephone,
      Company = company,
      Address1 = address1,
      Address2 = address2,
      CityStateZip = cityStateZip,
      Date = date,
      PixelData = pixelData,
    };
  }

  private static void _ParseDimensions(string value, out int width, out int height, out int depth) {
    width = 0;
    height = 0;
    depth = 0;

    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length >= 3) {
      int.TryParse(parts[0], out width);
      int.TryParse(parts[1], out height);
      int.TryParse(parts[2], out depth);
    }
  }

  private static bool _IsHexDigit(char c)
    => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

  private static int _HexValue(char c)
    => c switch {
      >= '0' and <= '9' => c - '0',
      >= 'A' and <= 'F' => c - 'A' + 10,
      >= 'a' and <= 'f' => c - 'a' + 10,
      _ => 0,
    };
}
