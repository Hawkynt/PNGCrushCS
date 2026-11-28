using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.LuraWave;

namespace FileFormat.LuraDocument;

/// <summary>Reads LuraDocument files from bytes, streams, or file paths, decoding and composing every page.</summary>
public static class LuraDocumentReader {

  public static LuraDocumentFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LuraDocument file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LuraDocumentFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static LuraDocumentFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  /// <exception cref="InvalidDataException">
  /// Not a LuraDocument file, a page that cannot be composed, or a layer coded in a way LuraTech's decoder does not
  /// handle (a mask in anything but JBIG, a layer in anything but LuraWave grey or three-channel).
  /// </exception>
  public static LuraDocumentFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var pages = new List<LuraDocumentPage>();
    foreach (var group in Group(LuraDocumentContainer.Read(data)))
      pages.Add(Compose(group));

    if (pages.Count == 0)
      throw new InvalidDataException("The LuraDocument file has no pages.");
    return new() { Pages = pages };
  }

  /// <summary>The directories of one page: its page directory and the layers that follow it.</summary>
  internal sealed record PageDirectories(LuraDocumentDirectory Page, LuraDocumentDirectory? Foreground, LuraDocumentDirectory? Background);

  /// <summary>
  /// Splits the directory chain into pages: a directory named <c>bit</c> (or one with no name) opens a page, and the
  /// <c>for</c> and <c>bac</c> directories after it are its foreground and background. Thumbnails (<c>thm</c>) and
  /// text layers (<c>txt</c>) are skipped.
  /// </summary>
  internal static List<PageDirectories> Group(List<LuraDocumentDirectory> directories) {
    var pages = new List<PageDirectories>();
    PageDirectories? current = null;
    foreach (var directory in directories) {
      switch (directory.Name) {
        case "" or "bit":
          if (current is not null)
            pages.Add(current);
          current = new(directory, null, null);
          break;
        case "for":
          current = (current ?? throw new InvalidDataException("A LuraDocument foreground layer comes before any page.")) with { Foreground = directory };
          break;
        case "bac":
          current = (current ?? throw new InvalidDataException("A LuraDocument background layer comes before any page.")) with { Background = directory };
          break;
      }
    }

    if (current is not null)
      pages.Add(current);
    return pages;
  }

  /// <summary>Decodes a page's layers and composes them the way LuraTech's decoder does.</summary>
  internal static LuraDocumentPage Compose(PageDirectories page) {
    var directory = page.Page;
    var width = directory.Width;
    var height = directory.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A LuraDocument page measures {width}x{height}.");
    if ((long)width * height > LuraWaveHeader.MaxPixels)
      throw new InvalidDataException($"A LuraDocument page of {width}x{height} is too large.");

    var (horizontalDpi, verticalDpi) = (_Dpi(directory.XResolution), _Dpi(directory.YResolution));
    byte[]? mask = null;
    switch (directory.Compression) {
      case LuraDocumentContainer.CompressionMask:
        // The resolution keys the mask's scrambling, so without it the mask cannot be read (LuraTech's decoder refuses too).
        if (horizontalDpi == 0 || verticalDpi == 0)
          throw new InvalidDataException("A LuraDocument page with a mask has no resolution, which its mask's scrambling depends on.");
        mask = _DecodeMask(directory, Math.Max(horizontalDpi, verticalDpi));
        break;
      case 0 or 1 when directory.Data.Length == 0:
        break;
      case 4:
        throw new InvalidDataException("A LuraDocument page whose mask is CCITT Group 4 (a plain fax page) is not supported.");
      default:
        throw new InvalidDataException($"A LuraDocument page mask with TIFF compression {directory.Compression} is not supported; LuraTech's masks are JBIG ({LuraDocumentContainer.CompressionMask}).");
    }

    // LuraTech's decoder composes a masked page only when it has a foreground: without one the mask is the page.
    var foreground = mask is not null && page.Foreground is { } f ? _DecodeLayer(f) : null;
    var background = (mask is null || foreground is not null) && page.Background is { } b ? _DecodeLayer(b) : null;
    if (mask is null && background is null)
      throw new InvalidDataException("A LuraDocument page has neither a mask nor a background layer.");

    var kind = foreground is null && background is null
      ? LuraDocumentPageKind.Bilevel
      : (foreground?.Channels ?? 1) == 3 || (background?.Channels ?? 1) == 3 ? LuraDocumentPageKind.Colour : LuraDocumentPageKind.Grey;

    var result = new LuraDocumentPage {
      Width = width,
      Height = height,
      Kind = kind,
      HorizontalDpi = horizontalDpi,
      VerticalDpi = verticalDpi,
      HasMask = mask is not null,
      ForegroundSize = foreground is { } fs ? (fs.Width, fs.Height) : null,
      BackgroundSize = background is { } bs ? (bs.Width, bs.Height) : null,
    };

    if (kind == LuraDocumentPageKind.Bilevel) {
      var bits = new byte[width * height];
      var stride = (width + 7) >> 3;
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        bits[y * width + x] = (byte)((mask![y * stride + (x >> 3)] >> (7 - (x & 7))) & 1);
      return result with { PixelData = bits };
    }

    var channels = kind == LuraDocumentPageKind.Colour ? 3 : 1;
    var pixels = background is { } back ? _Render(back, true, channels, width, height) : _Fill(255, width * height * channels);
    if (mask is not null) {
      var front = foreground is { } fore ? _Render(fore, false, channels, width, height) : _Fill(0, width * height * channels);
      var stride = (width + 7) >> 3;
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        // A clear mask bit is foreground: the mask is TIFF "black is zero", and black marks the text.
        if (((mask[y * stride + (x >> 3)] >> (7 - (x & 7))) & 1) != 0)
          continue;
        var i = ((long)y * width + x) * channels;
        for (var c = 0; c < channels; ++c)
          pixels[i + c] = front[i + c];
      }
    }

    return result with { PixelData = pixels };
  }

  private sealed record Layer(byte[] Samples, int Width, int Height, int Channels);

  private static byte[] _DecodeMask(LuraDocumentDirectory directory, int resolution) {
    if (directory.BitsPerSample != 1 || directory.SamplesPerPixel != 1)
      throw new InvalidDataException($"A LuraDocument mask has {directory.SamplesPerPixel} samples of {directory.BitsPerSample} bits; a mask is one bit.");

    var bie = directory.Data[..];
    LuraDocumentMask.Apply(bie, LuraDocumentMask.Seed(resolution, directory.Width, directory.Height));
    var (rows, width, height) = LuraDocumentJbig.Decode(bie);
    if (width != directory.Width || height < directory.Height)
      throw new InvalidDataException($"A LuraDocument mask of {width}x{height} does not cover its {directory.Width}x{directory.Height} page.");
    return rows;
  }

  private static Layer _DecodeLayer(LuraDocumentDirectory directory) {
    if (directory.Compression is not (LuraDocumentContainer.CompressionLuraWave or LuraDocumentContainer.CompressionLuraWaveLarge))
      throw new InvalidDataException($"A LuraDocument '{directory.Name}' layer with TIFF compression {directory.Compression} is not supported; LuraTech's layers are LuraWave ({LuraDocumentContainer.CompressionLuraWave}).");

    var wave = LuraWaveReader.FromBytes(directory.Data);
    var channels = wave.Mode switch {
      LuraWaveColourMode.Grey => 1,
      LuraWaveColourMode.Rgb => 3,
      _ => throw new InvalidDataException($"A LuraDocument '{directory.Name}' layer is LuraWave colour mode {(int)wave.Mode}; LuraTech's layers are grey (1) or three plain channels (4)."),
    };
    return new(wave.PixelData, wave.Width, wave.Height, channels);
  }

  private static byte[] _Render(Layer layer, bool invertedLuma, int channels, int pageWidth, int pageHeight) {
    var samples = layer.Samples[..];
    if (layer.Channels == 3)
      LuraDocumentColour.ToRgb(samples, invertedLuma);
    else {
      LuraDocumentColour.ToGrey(samples, invertedLuma);
      if (channels == 3)
        samples = LuraDocumentColour.GreyToRgb(samples);
    }

    var factor = LuraDocumentColour.Factor(pageWidth, pageHeight, layer.Width, layer.Height);
    return LuraDocumentColour.Enlarge(samples, layer.Width, layer.Height, channels, factor, pageWidth, pageHeight);
  }

  private static byte[] _Fill(byte value, long length) {
    var array = new byte[length];
    Array.Fill(array, value);
    return array;
  }

  private static int _Dpi((uint Numerator, uint Denominator)? rational)
    => rational is { Denominator: > 0 } r ? (int)Math.Min(r.Numerator / r.Denominator, int.MaxValue) : 0;
}
