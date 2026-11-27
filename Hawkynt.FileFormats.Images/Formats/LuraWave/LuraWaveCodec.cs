using System;
using System.IO;

namespace FileFormat.LuraWave;

/// <summary>Turns the coded data of a LuraWave stream into pixels and back.</summary>
internal static class LuraWaveCodec {

  /// <summary>Decodes all stripes, undoes the wavelet and converts to pixels.</summary>
  /// <returns>Grey8, Gray16 (big-endian) or Rgb24 pixels, depending on <see cref="LuraWaveFile.PixelFormatOf"/>.</returns>
  public static byte[] Decode(byte[] data, LuraWaveHeader header) {
    var declared = 0L;
    foreach (var size in header.StripeSizes)
      declared += size;
    if (header.DataOffset + declared > data.Length)
      throw new InvalidDataException($"LuraWave stream is truncated: its stripes declare {declared} bytes of coded data but only {data.Length - header.DataOffset} follow the header.");

    var channels = header.Channels;
    var width = header.CodedWidth;
    var stripeSamples = width * header.StripeHeight * channels;
    var codedHeight = header.StripeHeight * header.StripeCount;

    var plane = new int[(long)width * codedHeight * channels];
    var stripe = new int[stripeSamples];
    var reader = new LuraWaveBitReader(data, header.DataOffset);
    var decoder = new LuraWaveStripeCoder(header, reader);
    for (var index = 0; index < header.StripeCount; ++index) {
      if (index > 0) {
        if (reader.State == LuraWaveReadState.Exhausted)
          reader.State = LuraWaveReadState.Reading;
        if (!reader.SkipToStripeEnd())
          throw new InvalidDataException($"LuraWave stream ends before stripe {index} begins.");
      }

      decoder.Decode(stripe, index * header.StripeHeight);
      Array.Copy(stripe, 0, plane, (long)index * stripeSamples, stripeSamples);
    }

    for (var channel = 0; channel < channels; ++channel)
      LuraWaveWavelet.Inverse(plane, width, codedHeight, channels, channel, header.Levels);

    return ToPixels(plane, header);
  }

  private static byte[] ToPixels(int[] plane, LuraWaveHeader header) {
    var channels = header.Channels;
    var rowSamples = header.CodedWidth * channels;
    var mode = header.Mode;
    var (maxValue, shift) = LuraWaveColour.WideRange(header);
    switch (mode) {
      case LuraWaveColourMode.Grey: {
        var output = new byte[header.Width * header.Height];
        for (var y = 0; y < header.Height; ++y)
        for (var x = 0; x < header.Width; ++x)
          output[y * header.Width + x] = LuraWaveColour.Grey8(mode, plane, y * rowSamples + x, maxValue, shift);
        return output;
      }
      case LuraWaveColourMode.WideGrey or LuraWaveColourMode.WideSignedGrey: {
        var output = new byte[header.Width * header.Height * 2];
        for (var y = 0; y < header.Height; ++y)
        for (var x = 0; x < header.Width; ++x) {
          var value = LuraWaveColour.Grey16(mode, plane, y * rowSamples + x, header.MaxValue);
          var o = (y * header.Width + x) * 2;
          output[o] = (byte)(value >> 8);
          output[o + 1] = (byte)value;
        }
        return output;
      }
      default: {
        var output = new byte[header.Width * header.Height * 3];
        for (var y = 0; y < header.Height; ++y)
        for (var x = 0; x < header.Width; ++x)
          LuraWaveColour.Rgb(mode, plane, y * rowSamples + x * channels, shift, output, (y * header.Width + x) * 3);
        return output;
      }
    }
  }

  /// <summary>
  /// Where bit-plane coding hands over to verbatim bits. LuraTech's own files stop at plane 2; for lossless coding,
  /// sorting all the way down came out about two percent smaller on photographs, so the writer does that.
  /// </summary>
  internal const int DefaultLowestBitPlane = 0;

  /// <summary>Most wavelet levels the encoder uses — the depth LuraTech's own encoder chose.</summary>
  internal const int EncoderLevels = 6;

  /// <summary>
  /// Writes a complete single-stripe ("embedded") lossless stream: grey, 16-bit grey or reversible-colour RGB.
  /// </summary>
  /// <param name="pixels">Grey8, big-endian Gray16 or Rgb24 rows without padding.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="mode"><see cref="LuraWaveColourMode.Grey"/>, <see cref="LuraWaveColourMode.WideGrey"/> or <see cref="LuraWaveColourMode.ReversibleColour"/>.</param>
  /// <param name="metadataSource">A header whose resolution is carried over, or <see langword="null"/>.</param>
  public static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, LuraWaveColourMode mode, LuraWaveHeader? metadataSource = null) {
    if (mode is not (LuraWaveColourMode.Grey or LuraWaveColourMode.WideGrey or LuraWaveColourMode.ReversibleColour))
      throw new ArgumentException($"The writer codes grey, wide grey or reversible colour, not {mode}.", nameof(mode));

    var levels = EncoderLevels;
    while (levels > 2 && (1 << levels) > Math.Min(width, height))
      --levels;

    var block = 1 << levels;
    var codedWidth = (width + block - 1) / block * block;
    var codedHeight = (height + block - 1) / block * block;
    var header = new LuraWaveHeader {
      Width = width,
      Height = height,
      Mode = mode,
      CodedWidth = codedWidth,
      StripeHeight = codedHeight,
      Levels = levels,
      CodingFlags = 0x20,
      LowestBitPlane = DefaultLowestBitPlane,
      StripeCount = 1,
      MaxValue = mode == LuraWaveColourMode.WideGrey ? 65535 : 0,
      HorizontalDpiFixed = metadataSource?.HorizontalDpiFixed ?? LuraWaveHeader.DefaultDpiFixed,
      VerticalDpiFixed = metadataSource?.VerticalDpiFixed ?? LuraWaveHeader.DefaultDpiFixed,
    };

    var channels = header.Channels;
    var plane = new int[codedWidth * codedHeight * channels];
    for (var y = 0; y < codedHeight; ++y)
    for (var x = 0; x < codedWidth; ++x) {
      // Padding repeats the last row and column, which keeps the padded details small.
      var source = Math.Min(y, height - 1) * width + Math.Min(x, width - 1);
      var target = (y * codedWidth + x) * channels;
      switch (mode) {
        case LuraWaveColourMode.Grey:
          plane[target] = pixels[source] - 128;
          break;
        case LuraWaveColourMode.WideGrey:
          plane[target] = pixels[source * 2] << 8 | pixels[source * 2 + 1];
          break;
        default: {
          int r = pixels[source * 3], g = pixels[source * 3 + 1], b = pixels[source * 3 + 2];
          var u = r - g;
          var v = b - g;
          plane[target] = g + ((u + v) >> 2) - 128;
          plane[target + 1] = u;
          plane[target + 2] = v;
          break;
        }
      }
    }

    return EncodeSamples(header, plane);
  }

  /// <summary>
  /// Codes channel samples (interleaved, already in the mode's colour model and padded to the coded grid) under the
  /// given header, which fixes levels, stripes and interest regions; returns the complete file.
  /// </summary>
  internal static byte[] EncodeSamples(LuraWaveHeader header, int[] plane) {
    var channels = header.Channels;
    var width = header.CodedWidth;
    var codedHeight = header.StripeHeight * header.StripeCount;
    for (var channel = 0; channel < channels; ++channel)
      LuraWaveWavelet.Forward(plane, width, codedHeight, channels, channel, header.Levels);

    var stripeSamples = width * header.StripeHeight * channels;
    var stripe = new int[stripeSamples];
    var sizes = new int[header.StripeCount];
    using var coded = new System.IO.MemoryStream();
    for (var index = 0; index < header.StripeCount; ++index) {
      Array.Copy(plane, (long)index * stripeSamples, stripe, 0, stripeSamples);
      var writer = new LuraWaveBitWriter();
      new LuraWaveStripeCoder(header, writer).Encode(stripe, index * header.StripeHeight);
      var bytes = writer.Finish();
      sizes[index] = bytes.Length;
      coded.Write(bytes);
    }

    return (header with { StripeSizes = sizes }).Serialize(coded.ToArray());
  }
}
