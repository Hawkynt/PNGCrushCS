using System;
using System.IO;

namespace FileFormat.Neochrome;

/// <summary>Reads NEOchrome files from bytes, streams, or file paths.</summary>
public static class NeochromeReader {

  public static NeochromeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NEOchrome file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeochromeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[checked((int)(stream.Length - stream.Position))];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static NeochromeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NeochromeHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid NEOchrome header.");

    NeochromeHeader header;
    try {
      header = NeochromeHeader.ReadFrom(data);
    } catch (ArgumentException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    (int Width, int Height, int Planes) mode;
    try {
      mode = NeochromeFile.GetMode(header.Flag, header.Resolution);
    } catch (ArgumentException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    var virtualCanvas = unchecked((ushort)header.Flag) == NeochromeFile.VirtualCanvasFlag;
    var expectedStoredWidth = virtualCanvas ? (short)640 : (short)320;
    var expectedStoredHeight = virtualCanvas ? (short)400 : (short)200;
    if (header.AnimXOffset != 0 || header.AnimYOffset != 0)
      throw new InvalidDataException("NEOchrome image offsets must both be zero.");
    if (header.AnimWidth != expectedStoredWidth || header.AnimHeight != expectedStoredHeight)
      throw new InvalidDataException($"NEOchrome header dimensions must be exactly {expectedStoredWidth}x{expectedStoredHeight} for this variant.");

    var expectedRaster = checked(((mode.Width + 15) / 16) * mode.Planes * 2 * mode.Height);
    var expectedLength = checked(NeochromeHeader.StructSize + expectedRaster);
    if (data.Length != expectedLength)
      throw new InvalidDataException($"NEOchrome file length must be exactly {expectedLength} bytes for the declared variant.");

    var file = new NeochromeFile {
      Width = mode.Width,
      Height = mode.Height,
      Flag = header.Flag,
      Resolution = header.Resolution,
      Palette = header.GetPalette(),
      FileName = header.FileName,
      AnimationLimits = header.AnimationLimits,
      AnimSpeed = header.AnimSpeed,
      AnimDirection = header.AnimDirection,
      AnimSteps = header.AnimSteps,
      AnimXOffset = header.AnimXOffset,
      AnimYOffset = header.AnimYOffset,
      AnimWidth = header.AnimWidth,
      AnimHeight = header.AnimHeight,
      Reserved = header.Reserved,
      PixelData = data[NeochromeHeader.StructSize..].ToArray(),
    };

    try {
      NeochromeFile.Validate(file, nameof(data));
    } catch (ArgumentException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    return file;
  }

  public static NeochromeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
