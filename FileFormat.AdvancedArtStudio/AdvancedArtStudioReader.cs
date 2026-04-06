using System;
using System.IO;

namespace FileFormat.AdvancedArtStudio;

/// <summary>Reads Advanced Art Studio (.ocp) files from bytes, streams, or file paths.</summary>
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

  public static AdvancedArtStudioFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AdvancedArtStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AdvancedArtStudioFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Advanced Art Studio file (expected {AdvancedArtStudioFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += AdvancedArtStudioFile.LoadAddressSize;

    var bitmapData = new byte[AdvancedArtStudioFile.BitmapDataSize];
    data.AsSpan(offset, AdvancedArtStudioFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += AdvancedArtStudioFile.BitmapDataSize;

    var screenRam = new byte[AdvancedArtStudioFile.ScreenRamSize];
    data.AsSpan(offset, AdvancedArtStudioFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += AdvancedArtStudioFile.ScreenRamSize;

    var colorRam = new byte[AdvancedArtStudioFile.ColorRamSize];
    data.AsSpan(offset, AdvancedArtStudioFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += AdvancedArtStudioFile.ColorRamSize;

    var backgroundColor = data[offset];
    ++offset;

    var borderColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BorderColor = borderColor,
    };
  }
}
