using System;
using System.IO;

namespace FileFormat.AdvancedArtStudio;

/// <summary>Reads Advanced Art Studio (.ocp) files in either multicolor (10018 bytes) or hi-res (9009 bytes) layout.</summary>
public static class AdvancedArtStudioReader {

  public static AdvancedArtStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Advanced Art Studio file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AdvancedArtStudioFile FromStream(Stream stream) {
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

  public static AdvancedArtStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AdvancedArtStudioFile FromSpan(ReadOnlySpan<byte> data) {
    // Both variants share the same load address + bitmap + screen-RAM prefix. Anything below
    // the hi-res minimum can't be a valid OCP file.
    const int hiResMin = AdvancedArtStudioFile.LoadAddressSize
                       + AdvancedArtStudioFile.BitmapDataSize
                       + AdvancedArtStudioFile.ScreenRamSize; // 9002, ignoring trailing
    if (data.Length < hiResMin)
      throw new InvalidDataException($"Data too small for a valid Advanced Art Studio file (got {data.Length} bytes; need at least {hiResMin}).");

    var isHiRes = data.Length < AdvancedArtStudioFile.MulticolorFileSize;
    var offset = 0;
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += AdvancedArtStudioFile.LoadAddressSize;

    var bitmapData = data.Slice(offset, AdvancedArtStudioFile.BitmapDataSize).ToArray();
    offset += AdvancedArtStudioFile.BitmapDataSize;

    var screenRam = data.Slice(offset, AdvancedArtStudioFile.ScreenRamSize).ToArray();
    offset += AdvancedArtStudioFile.ScreenRamSize;

    if (isHiRes) {
      // Hi-res layout: no colour RAM. Trailing bytes carry border colour (the canonical 7-byte
      // tail uses the last byte as the border colour; older writers may omit it entirely).
      var borderHiRes = data.Length > offset ? data[^1] : (byte)0;
      return new AdvancedArtStudioFile {
        IsHiRes = true,
        LoadAddress = loadAddress,
        BitmapData = bitmapData,
        ScreenRam = screenRam,
        ColorRam = [],
        BackgroundColor = 0,
        BorderColor = borderHiRes,
      };
    }

    var colorRam = data.Slice(offset, AdvancedArtStudioFile.ColorRamSize).ToArray();
    offset += AdvancedArtStudioFile.ColorRamSize;
    var backgroundColor = data[offset];
    var borderColor = data[offset + 1];

    return new AdvancedArtStudioFile {
      IsHiRes = false,
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BorderColor = borderColor,
    };
  }
}
