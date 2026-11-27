using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Pdf.Document;

/// <summary>The general-purpose stream filters of PDF 32000-1 §7.4, which turn bytes into bytes.</summary>
/// <remarks>
/// The image filters — DCT, JPX, CCITT fax and JBIG2 — decode to pictures rather than to bytes and
/// need the image dictionary to do it, so the chain stops in front of one and hands it to the image
/// decoder with whatever came out of the filters before it. Everything here is written from the
/// specification's own description of each filter (rung 3 of the sourcing ladder): they are short,
/// exactly specified, and the predictors are the PNG and TIFF ones this package already reads
/// elsewhere.
/// <para/>
/// Damaged data is decoded as far as it goes. A viewer shows the part of a page whose stream was cut
/// short, and an exception here would show none of it.
/// </remarks>
internal static class PdfFilters {

  /// <summary>Whether a filter name is one that decodes to a picture.</summary>
  public static bool IsImageFilter(string name) => name is "DCTDecode" or "DCT" or "JPXDecode" or "CCITTFaxDecode" or "CCF" or "JBIG2Decode";

  /// <summary>The filter names and their parameters, in the order they apply.</summary>
  public static List<(string Name, PdfDict? Parameters)> FiltersOf(PdfDocument document, PdfDict dict, bool inline = false) {
    var result = new List<(string, PdfDict?)>();
    var filter = document.Resolve(dict.Raw("Filter") ?? (inline ? dict.Raw("F") : null));
    var parms = document.Resolve(dict.Raw("DecodeParms") ?? (inline ? dict.Raw("DP") : null));
    if (filter is string single) {
      result.Add((_LongName(single), document.Resolve(parms is List<object?> one && one.Count > 0 ? one[0] : parms) as PdfDict));
      return result;
    }

    if (filter is not List<object?> list)
      return result;

    for (var i = 0; i < list.Count; ++i) {
      if (document.Resolve(list[i]) is not string name)
        continue;

      var p = parms is List<object?> pl ? (i < pl.Count ? document.Resolve(pl[i]) : null) : (i == 0 ? parms : null);
      result.Add((_LongName(name), p as PdfDict));
    }

    return result;
  }

  private static string _LongName(string name) => name switch {
    "AHx" => "ASCIIHexDecode",
    "A85" => "ASCII85Decode",
    "LZW" => "LZWDecode",
    "Fl" => "FlateDecode",
    "RL" => "RunLengthDecode",
    "CCF" => "CCITTFaxDecode",
    "DCT" => "DCTDecode",
    _ => name,
  };

  /// <summary>
  /// Runs the byte filters, stopping in front of the first image filter, which is returned with its
  /// parameters for the caller to apply.
  /// </summary>
  public static byte[] Decode(PdfDocument document, PdfDict dict, byte[] data, out string? imageFilter, out PdfDict? imageParameters, bool inline = false) {
    imageFilter = null;
    imageParameters = null;
    foreach (var (name, parameters) in FiltersOf(document, dict, inline)) {
      if (IsImageFilter(name)) {
        imageFilter = name;
        imageParameters = parameters;
        return data;
      }

      data = Apply(document, name, parameters, data);
    }

    return data;
  }

  /// <summary>Applies one byte filter.</summary>
  public static byte[] Apply(PdfDocument document, string name, PdfDict? parameters, byte[] data) => name switch {
    "FlateDecode" => _Predict(document, Inflate(data), parameters),
    "LZWDecode" => _Predict(document, Lzw(data, PdfValues.Integer(document.Resolve(parameters?.Raw("EarlyChange")), 1) != 0), parameters),
    "ASCII85Decode" => Ascii85(data),
    "ASCIIHexDecode" => AsciiHex(data),
    "RunLengthDecode" => RunLength(data),
    "Crypt" => data, // Decryption already happened when the stream was read; only Identity reaches here.
    _ => throw new NotSupportedException($"The PDF stream filter /{name} is not supported."),
  };

  /// <summary>zlib/deflate, keeping whatever came out before the data went bad.</summary>
  public static byte[] Inflate(byte[] data) {
    if (data.Length == 0)
      return data;

    // The two-byte zlib header is skipped by hand rather than left to ZLibStream so that a stream
    // whose header is wrong — or missing, which some writers manage — still inflates.
    var offset = data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0 ? 2 : 0;
    var output = new MemoryStream(Math.Max(64, data.Length * 3));
    try {
      using var input = new MemoryStream(data, offset, data.Length - offset);
      using var inflater = new DeflateStream(input, CompressionMode.Decompress);
      var buffer = new byte[16384];
      int read;
      while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
        output.Write(buffer, 0, read);
    } catch (InvalidDataException) {
      // Truncated or corrupt: keep what decoded.
    }

    return output.ToArray();
  }

  /// <summary>LZW as PDF uses it: MSB first, codes from 9 to 12 bits, optionally switching a code early.</summary>
  public static byte[] Lzw(byte[] data, bool earlyChange) {
    var output = new List<byte>(data.Length * 3);
    var prefix = new int[4096];
    var suffix = new byte[4096];
    var length = new int[4096];
    for (var i = 0; i < 256; ++i) {
      prefix[i] = -1;
      suffix[i] = (byte)i;
      length[i] = 1;
    }

    var next = 258;
    var codeLength = 9;
    var previous = -1;
    var bitBuffer = 0;
    var bitCount = 0;
    var pos = 0;
    var scratch = new byte[4096];

    while (true) {
      while (bitCount < codeLength) {
        if (pos >= data.Length)
          return output.ToArray();
        bitBuffer = (bitBuffer << 8) | data[pos++];
        bitCount += 8;
      }

      var code = (bitBuffer >> (bitCount - codeLength)) & ((1 << codeLength) - 1);
      bitCount -= codeLength;

      if (code == 256) {
        next = 258;
        codeLength = 9;
        previous = -1;
        continue;
      }

      if (code == 257)
        break;

      int first;
      if (code < next && (code < 256 || code >= 258)) {
        first = _Emit(code, prefix, suffix, length, scratch, output);
      } else if (code == next && previous >= 0) {
        first = _Emit(previous, prefix, suffix, length, scratch, output);
        output.Add((byte)first);
      } else
        break;

      if (previous >= 0 && next < 4096) {
        prefix[next] = previous;
        suffix[next] = (byte)first;
        length[next] = length[previous] + 1;
        ++next;
      }

      var threshold = earlyChange ? next + 1 : next;
      codeLength = threshold >= 2048 ? 12 : threshold >= 1024 ? 11 : threshold >= 512 ? 10 : 9;
      previous = code;
    }

    return output.ToArray();
  }

  private static int _Emit(int code, int[] prefix, byte[] suffix, int[] length, byte[] scratch, List<byte> output) {
    var count = length[code];
    for (var i = count - 1; i >= 0; --i) {
      scratch[i] = suffix[code];
      code = prefix[code];
    }

    for (var i = 0; i < count; ++i)
      output.Add(scratch[i]);

    return scratch[0];
  }

  /// <summary>ASCII base-85, with the <c>z</c> shorthand and a short final group.</summary>
  public static byte[] Ascii85(byte[] data) {
    var output = new List<byte>(data.Length * 4 / 5 + 4);
    var group = 0L;
    var count = 0;
    var i = 0;
    if (data.Length >= 2 && data[0] == (byte)'<' && data[1] == (byte)'~')
      i = 2;

    for (; i < data.Length; ++i) {
      var b = data[i];
      if (b == (byte)'~')
        break;
      if (PdfLexer.IsWhitespace(b))
        continue;
      if (b == (byte)'z' && count == 0) {
        output.Add(0);
        output.Add(0);
        output.Add(0);
        output.Add(0);
        continue;
      }

      if (b is < 33 or > 117)
        continue;

      group = group * 85 + (b - 33);
      if (++count < 5)
        continue;

      output.Add((byte)(group >> 24));
      output.Add((byte)(group >> 16));
      output.Add((byte)(group >> 8));
      output.Add((byte)group);
      group = 0;
      count = 0;
    }

    if (count > 1) {
      for (var j = count; j < 5; ++j)
        group = group * 85 + 84;
      for (var j = 0; j < count - 1; ++j)
        output.Add((byte)(group >> (24 - 8 * j)));
    }

    return output.ToArray();
  }

  /// <summary>Hexadecimal pairs up to <c>&gt;</c>, white space ignored, an odd last digit padded with zero.</summary>
  public static byte[] AsciiHex(byte[] data) {
    var output = new List<byte>(data.Length / 2 + 1);
    var high = -1;
    foreach (var b in data) {
      if (b == (byte)'>')
        break;

      var value = b switch {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
      };
      if (value < 0)
        continue;

      if (high < 0)
        high = value;
      else {
        output.Add((byte)(high << 4 | value));
        high = -1;
      }
    }

    if (high >= 0)
      output.Add((byte)(high << 4));

    return output.ToArray();
  }

  /// <summary>The PackBits-style run-length coding, ended by 128.</summary>
  public static byte[] RunLength(byte[] data) {
    var output = new List<byte>(data.Length * 2);
    var i = 0;
    while (i < data.Length) {
      var length = data[i++];
      if (length == 128)
        break;

      if (length < 128) {
        for (var n = 0; n <= length && i < data.Length; ++n)
          output.Add(data[i++]);
      } else if (i < data.Length) {
        var value = data[i++];
        for (var n = 0; n < 257 - length; ++n)
          output.Add(value);
      }
    }

    return output.ToArray();
  }

  private static byte[] _Predict(PdfDocument document, byte[] data, PdfDict? parameters) {
    if (parameters == null)
      return data;

    var predictor = PdfValues.Integer(document.Resolve(parameters.Raw("Predictor")), 1);
    if (predictor <= 1)
      return data;

    var colors = Math.Max(1, PdfValues.Integer(document.Resolve(parameters.Raw("Colors")), 1));
    var bits = Math.Max(1, PdfValues.Integer(document.Resolve(parameters.Raw("BitsPerComponent")), 8));
    var columns = Math.Max(1, PdfValues.Integer(document.Resolve(parameters.Raw("Columns")), 1));
    var rowBytes = (int)(((long)colors * bits * columns + 7) / 8);
    var pixelBytes = Math.Max(1, (colors * bits + 7) / 8);

    return predictor == 2 ? _TiffPredict(data, rowBytes, colors, bits, columns) : _PngPredict(data, rowBytes, pixelBytes);
  }

  private static byte[] _PngPredict(byte[] data, int rowBytes, int pixelBytes) {
    var rows = (data.Length + rowBytes) / (rowBytes + 1);
    var output = new byte[rows * rowBytes];
    var previous = new byte[rowBytes];
    var current = new byte[rowBytes];
    var produced = 0;
    for (var row = 0; row < rows; ++row) {
      var at = row * (rowBytes + 1);
      if (at >= data.Length)
        break;

      var type = data[at];
      var available = Math.Min(rowBytes, data.Length - at - 1);
      Array.Clear(current);
      if (available > 0)
        Array.Copy(data, at + 1, current, 0, available);

      for (var i = 0; i < rowBytes; ++i) {
        var left = i >= pixelBytes ? current[i - pixelBytes] : 0;
        var up = previous[i];
        var upLeft = i >= pixelBytes ? previous[i - pixelBytes] : 0;
        current[i] = type switch {
          1 => (byte)(current[i] + left),
          2 => (byte)(current[i] + up),
          3 => (byte)(current[i] + ((left + up) >> 1)),
          4 => (byte)(current[i] + _Paeth(left, up, upLeft)),
          _ => current[i],
        };
      }

      current.CopyTo(output, row * rowBytes);
      (previous, current) = (current, previous);
      produced = row + 1;
    }

    return produced == rows ? output : output[..(produced * rowBytes)];
  }

  private static int _Paeth(int a, int b, int c) {
    var p = a + b - c;
    var pa = Math.Abs(p - a);
    var pb = Math.Abs(p - b);
    var pc = Math.Abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
  }

  private static byte[] _TiffPredict(byte[] data, int rowBytes, int colors, int bits, int columns) {
    var output = (byte[])data.Clone();
    var rows = data.Length / rowBytes;
    for (var row = 0; row < rows; ++row) {
      var at = row * rowBytes;
      switch (bits) {
        case 8:
          for (var i = colors; i < rowBytes; ++i)
            output[at + i] = (byte)(output[at + i] + output[at + i - colors]);
          break;
        case 16:
          for (var i = colors * 2; i + 1 < rowBytes; i += 2) {
            var value = ((output[at + i] << 8) | output[at + i + 1]) + ((output[at + i - colors * 2] << 8) | output[at + i - colors * 2 + 1]);
            output[at + i] = (byte)(value >> 8);
            output[at + i + 1] = (byte)value;
          }
          break;
        default: {
          // Sub-byte samples: walk them as a bit string, each sample adding the one a pixel back.
          var mask = (1 << bits) - 1;
          var previous = new int[colors];
          var samples = columns * colors;
          for (var s = 0; s < samples; ++s) {
            var bit = s * bits;
            var shift = 8 - bits - (bit & 7);
            var value = (output[at + (bit >> 3)] >> shift) & mask;
            value = (value + previous[s % colors]) & mask;
            previous[s % colors] = value;
            output[at + (bit >> 3)] = (byte)((output[at + (bit >> 3)] & ~(mask << shift)) | (value << shift));
          }
          break;
        }
      }
    }

    return output;
  }
}
