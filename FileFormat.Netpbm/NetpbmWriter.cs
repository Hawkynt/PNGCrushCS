using System;
using System.IO;
using System.Text;

namespace FileFormat.Netpbm;

/// <summary>Assembles Netpbm file bytes from pixel data.</summary>
public static class NetpbmWriter {

  public static byte[] ToBytes(NetpbmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Format switch {
      NetpbmFormat.PbmAscii => _WritePbmAscii(file),
      NetpbmFormat.PgmAscii => _WriteAscii(file),
      NetpbmFormat.PpmAscii => _WriteAscii(file),
      NetpbmFormat.PbmBinary => _WritePbmBinary(file),
      NetpbmFormat.PgmBinary or NetpbmFormat.PpmBinary => _WriteBinary(file),
      NetpbmFormat.Pam => _WritePam(file),
      _ => throw new InvalidDataException($"Unsupported Netpbm format: {file.Format}.")
    };
  }

  private static byte[] _WritePbmAscii(NetpbmFile file) {
    using var ms = new MemoryStream();
    _WriteString(ms, $"P1\n{file.Width} {file.Height}\n");

    var idx = 0;
    for (var row = 0; row < file.Height; ++row) {
      for (var col = 0; col < file.Width; ++col) {
        if (col > 0)
          ms.WriteByte((byte)' ');

        ms.WriteByte(idx < file.PixelData.Length && file.PixelData[idx] != 0 ? (byte)'1' : (byte)'0');
        ++idx;
      }
      ms.WriteByte((byte)'\n');
    }

    return ms.ToArray();
  }

  private static byte[] _WriteAscii(NetpbmFile file) {
    using var ms = new MemoryStream();
    var magic = file.Format == NetpbmFormat.PgmAscii ? "P2" : "P3";
    _WriteString(ms, $"{magic}\n{file.Width} {file.Height}\n{file.MaxValue}\n");

    var bytesPerSample = file.MaxValue > 255 ? 2 : 1;
    var totalSamples = file.Width * file.Height * file.Channels;
    var samplesPerRow = file.Width * file.Channels;
    var sampleIdx = 0;

    for (var row = 0; row < file.Height; ++row) {
      for (var col = 0; col < samplesPerRow; ++col) {
        if (col > 0)
          ms.WriteByte((byte)' ');

        int value;
        if (bytesPerSample == 2 && sampleIdx * 2 + 1 < file.PixelData.Length)
          value = (file.PixelData[sampleIdx * 2] << 8) | file.PixelData[sampleIdx * 2 + 1];
        else if (sampleIdx < file.PixelData.Length)
          value = file.PixelData[sampleIdx];
        else
          value = 0;

        _WriteString(ms, value.ToString());
        ++sampleIdx;
      }
      ms.WriteByte((byte)'\n');
    }

    return ms.ToArray();
  }

  private static byte[] _WritePbmBinary(NetpbmFile file) {
    using var ms = new MemoryStream();
    _WriteString(ms, $"P4\n{file.Width} {file.Height}\n");

    var bytesPerRow = (file.Width + 7) / 8;
    var pixelIdx = 0;

    for (var row = 0; row < file.Height; ++row) {
      for (var byteIdx = 0; byteIdx < bytesPerRow; ++byteIdx) {
        byte packed = 0;
        for (var bit = 7; bit >= 0; --bit) {
          var col = byteIdx * 8 + (7 - bit);
          if (col < file.Width && pixelIdx < file.PixelData.Length) {
            if (file.PixelData[pixelIdx] != 0)
              packed |= (byte)(1 << bit);

            ++pixelIdx;
          }
        }
        ms.WriteByte(packed);
      }
    }

    return ms.ToArray();
  }

  private static byte[] _WriteBinary(NetpbmFile file) {
    using var ms = new MemoryStream();
    var magic = file.Format == NetpbmFormat.PgmBinary ? "P5" : "P6";
    _WriteString(ms, $"{magic}\n{file.Width} {file.Height}\n{file.MaxValue}\n");
    ms.Write(file.PixelData, 0, file.PixelData.Length);
    return ms.ToArray();
  }

  private static byte[] _WritePam(NetpbmFile file) {
    using var ms = new MemoryStream();
    var sb = new StringBuilder();
    sb.Append("P7\n");
    sb.Append($"WIDTH {file.Width}\n");
    sb.Append($"HEIGHT {file.Height}\n");
    sb.Append($"DEPTH {file.Channels}\n");
    sb.Append($"MAXVAL {file.MaxValue}\n");
    if (file.TupleType != null)
      sb.Append($"TUPLTYPE {file.TupleType}\n");

    sb.Append("ENDHDR\n");
    _WriteString(ms, sb.ToString());
    ms.Write(file.PixelData, 0, file.PixelData.Length);
    return ms.ToArray();
  }

  private static void _WriteString(Stream ms, string s) {
    var bytes = Encoding.ASCII.GetBytes(s);
    ms.Write(bytes, 0, bytes.Length);
  }
}
