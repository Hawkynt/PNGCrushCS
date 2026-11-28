using System;
using System.IO;

namespace FileFormat.LuraWave;

/// <summary>Turns the coded data of a LuraWave stream into pixels and back.</summary>
internal static class LuraWaveCodec {

  /// <summary>Decodes all stripes, undoes the wavelet and converts to pixels.</summary>
  /// <returns>
  /// Pixels in <see cref="LuraWaveFile.PixelFormatOf"/> (Gray8, big-endian Gray16, Rgb24 or Rgba32), and for the modes
  /// LuraTech's decoder returns only raw, the channel samples it returns (see <see cref="LuraWaveFile.Channels"/>).
  /// </returns>
  /// <param name="data">The whole file.</param>
  /// <param name="header">Its parsed header.</param>
  /// <param name="passwordCheck">The 32-bit check of an accepted password, or <see langword="null"/> for the public quality.</param>
  public static (byte[] Pixels, ushort[]? Channels) Decode(byte[] data, LuraWaveHeader header, uint? passwordCheck = null) {
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
    var decoder = new LuraWaveStripeCoder(header, reader, passwordCheck);
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

    var samples = ToChannels(plane, header);
    return (samples is null ? ToPixels(plane, header) : ChannelsToPixels(samples, header), samples);
  }

  /// <summary>
  /// The channel samples LuraTech's decoder hands back for the modes it returns only raw (its output types 2, 0x23 and
  /// 0x24): the colour transform undone, clamped to 0..255 or to the stream's maximum, interleaved, cropped to the
  /// visible size. <see langword="null"/> for every other mode.
  /// </summary>
  private static ushort[]? ToChannels(int[] plane, LuraWaveHeader header) {
    var mode = header.Mode;
    if (mode is not (LuraWaveColourMode.RawFourChannel or LuraWaveColourMode.WideLab or LuraWaveColourMode.WideFourChannel))
      return null;

    var channels = header.Channels;
    var rowSamples = header.CodedWidth * channels;
    var maximum = mode == LuraWaveColourMode.RawFourChannel ? 255 : LuraWaveColour.WideMaximum(header);
    var output = new ushort[header.Width * header.Height * channels];
    for (var y = 0; y < header.Height; ++y)
    for (var x = 0; x < header.Width; ++x) {
      var i = y * rowSamples + x * channels;
      var o = (y * header.Width + x) * channels;
      switch (mode) {
        case LuraWaveColourMode.RawFourChannel:
          for (var c = 0; c < 4; ++c)
            output[o + c] = (ushort)Math.Clamp(plane[i + c] + 128, 0, maximum);
          break;
        case LuraWaveColourMode.WideLab:
          for (var c = 0; c < 3; ++c)
            output[o + c] = (ushort)Math.Clamp(plane[i + c], 0, maximum);
          break;
        default: {
          // The reversible colour transform on the first three channels, the fourth passed through.
          var g = plane[i] - ((plane[i + 1] + plane[i + 2]) >> 2);
          output[o] = (ushort)Math.Clamp(plane[i + 1] + g, 0, maximum);
          output[o + 1] = (ushort)Math.Clamp(g, 0, maximum);
          output[o + 2] = (ushort)Math.Clamp(plane[i + 2] + g, 0, maximum);
          output[o + 3] = (ushort)Math.Clamp(plane[i + 3], 0, maximum);
          break;
        }
      }
    }

    return output;
  }

  private static byte[] ChannelsToPixels(ushort[] samples, LuraWaveHeader header) {
    var count = header.Width * header.Height;
    switch (header.Mode) {
      case LuraWaveColourMode.RawFourChannel: {
        var output = new byte[count * 4];
        for (var i = 0; i < output.Length; ++i)
          output[i] = (byte)samples[i];
        return output;
      }
      case LuraWaveColourMode.WideLab: {
        var shift = LuraWaveColour.WideRange(header).Shift;
        var output = new byte[count * 3];
        for (var p = 0; p < count; ++p)
          LuraWaveColour.SrgbFromWideLab(samples, p * 3, shift, output, p * 3);
        return output;
      }
      default: {
        var shift = LuraWaveColour.WideRange(header).Shift;
        var output = new byte[count * 3];
        for (var p = 0; p < count; ++p)
          LuraWaveColour.RgbFromWideFourChannel(samples, p * 4, shift, output, p * 3);
        return output;
      }
    }
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
          var value = LuraWaveColour.Grey16(mode, plane, y * rowSamples + x, LuraWaveColour.WideMaximum(header));
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
  /// Where bit-plane coding hands over to verbatim bits: plane 2, as in LuraTech's own files. Sorting all the way down
  /// is about two percent smaller, but at the last planes LuraTech's two decoders disagree about nodes whose
  /// thresholds have all run out (the C decoder gives them up without a bit, the Java one still reads one); stopping
  /// at plane 2 keeps every such case out of a stream whose channel weights are at most one, so both decode it exactly.
  /// </summary>
  internal const int DefaultLowestBitPlane = 2;

  /// <summary>Most wavelet levels the encoder uses — the depth LuraTech's own encoder chose.</summary>
  internal const int EncoderLevels = 6;

  /// <summary>
  /// Writes a complete single-stripe ("embedded") lossless stream: grey, 16-bit grey, reversible-colour RGB, three
  /// plain channels (the layout LuraDocument stores its layers in) or four raw 8-bit channels.
  /// </summary>
  /// <param name="pixels">Grey8, big-endian Gray16, three-byte or Rgba32 rows without padding.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="mode">
  /// <see cref="LuraWaveColourMode.Grey"/>, <see cref="LuraWaveColourMode.WideGrey"/>,
  /// <see cref="LuraWaveColourMode.ReversibleColour"/>, <see cref="LuraWaveColourMode.Rgb"/> or
  /// <see cref="LuraWaveColourMode.RawFourChannel"/>.
  /// </param>
  /// <param name="metadataSource">A header whose resolution is carried over, or <see langword="null"/>.</param>
  public static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, LuraWaveColourMode mode, LuraWaveHeader? metadataSource = null) {
    if (mode is not (LuraWaveColourMode.Grey or LuraWaveColourMode.WideGrey or LuraWaveColourMode.ReversibleColour or LuraWaveColourMode.Rgb or LuraWaveColourMode.RawFourChannel))
      throw new ArgumentException($"The writer codes grey, wide grey, reversible colour, three plain channels or four raw channels from pixels, not {mode}.", nameof(mode));

    var header = EmbeddedHeader(width, height, mode, mode == LuraWaveColourMode.WideGrey ? 65535 : 0, metadataSource);
    var channels = header.Channels;
    var codedWidth = header.CodedWidth;
    var codedHeight = header.StripeHeight;
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
        case LuraWaveColourMode.Rgb:
          // Mode 4, "3x8 bit raw": each byte biased by -128 and no colour transform.
          for (var c = 0; c < 3; ++c)
            plane[target + c] = pixels[source * 3 + c] - 128;
          break;
        case LuraWaveColourMode.RawFourChannel:
          for (var c = 0; c < 4; ++c)
            plane[target + c] = pixels[source * 4 + c] - 128;
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
  /// Writes a complete single-stripe lossless stream from channel samples laid out as <see cref="LuraWaveFile.Channels"/>,
  /// in one of the modes LuraTech's decoder returns only raw. The forward step is LuraTech's encoder's: the 8-bit bias
  /// for <see cref="LuraWaveColourMode.RawFourChannel"/>, none for <see cref="LuraWaveColourMode.WideLab"/>, and the
  /// reversible colour transform on the first three channels of <see cref="LuraWaveColourMode.WideFourChannel"/>.
  /// </summary>
  /// <param name="samples">Interleaved samples, <c>width·height·channels</c> of them.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="mode">The colour mode.</param>
  /// <param name="maxValue">The <c>MV</c> maximum of a wide mode, or 0 for none (65535).</param>
  /// <param name="metadataSource">A header whose resolution is carried over, or <see langword="null"/>.</param>
  public static byte[] EncodeChannels(ReadOnlySpan<ushort> samples, int width, int height, LuraWaveColourMode mode, int maxValue, LuraWaveHeader? metadataSource = null) {
    if (mode is not (LuraWaveColourMode.RawFourChannel or LuraWaveColourMode.WideLab or LuraWaveColourMode.WideFourChannel))
      throw new ArgumentException($"Channel samples are written in modes 5, 20 or 21, not {mode}.", nameof(mode));

    var header = EmbeddedHeader(width, height, mode, mode == LuraWaveColourMode.RawFourChannel ? 0 : maxValue, metadataSource);
    var channels = header.Channels;
    var codedWidth = header.CodedWidth;
    var codedHeight = header.StripeHeight;
    var plane = new int[codedWidth * codedHeight * channels];
    for (var y = 0; y < codedHeight; ++y)
    for (var x = 0; x < codedWidth; ++x) {
      var source = (Math.Min(y, height - 1) * width + Math.Min(x, width - 1)) * channels;
      var target = (y * codedWidth + x) * channels;
      switch (mode) {
        case LuraWaveColourMode.RawFourChannel:
          for (var c = 0; c < 4; ++c)
            plane[target + c] = samples[source + c] - 128;
          break;
        case LuraWaveColourMode.WideLab:
          for (var c = 0; c < 3; ++c)
            plane[target + c] = samples[source + c];
          break;
        default: {
          int r = samples[source], g = samples[source + 1], b = samples[source + 2];
          plane[target] = (r + 2 * g + b) >> 2;
          plane[target + 1] = r - g;
          plane[target + 2] = b - g;
          plane[target + 3] = samples[source + 3];
          break;
        }
      }
    }

    return EncodeSamples(header, plane);
  }

  private static LuraWaveHeader EmbeddedHeader(int width, int height, LuraWaveColourMode mode, int maxValue, LuraWaveHeader? metadataSource) {
    var levels = EncoderLevels;
    while (levels > 2 && (1 << levels) > Math.Min(width, height))
      --levels;

    var block = 1 << levels;
    var codedWidth = (width + block - 1) / block * block;
    var codedHeight = (height + block - 1) / block * block;
    return new() {
      Width = width,
      Height = height,
      Mode = mode,
      CodedWidth = codedWidth,
      StripeHeight = codedHeight,
      Levels = levels,
      CodingFlags = 0x20,
      LowestBitPlane = LuraWaveStripeCoder.IsLosslessLowestPlane(DefaultLowestBitPlane, levels) ? DefaultLowestBitPlane : 0,
      StripeCount = 1,
      MaxValue = maxValue,
      HorizontalDpiFixed = metadataSource?.HorizontalDpiFixed ?? LuraWaveHeader.DefaultDpiFixed,
      VerticalDpiFixed = metadataSource?.VerticalDpiFixed ?? LuraWaveHeader.DefaultDpiFixed,
    };
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
    // Bit 0 of the byte after the colour mode marks the striped ("baseline") layout, as LuraTech's encoder sets it.
    header = header with { ModeFlags = (byte)(header.StripeCount > 1 ? header.ModeFlags | 1 : header.ModeFlags & ~1) };
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
