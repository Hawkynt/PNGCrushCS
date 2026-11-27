using System;
using System.IO;

namespace FileFormat.Iwc;

/// <summary>Reads IWC files.</summary>
internal static class IwcReader {

  public static IwcFile FromSpan(ReadOnlySpan<byte> data) {
    var header = IwcHeader.Parse(data);
    var payloadStart = IwcHeader.SizeFor(header.Levels);
    if (data.Length - payloadStart < header.PayloadLength)
      throw new InvalidDataException($"IWC payload is {header.PayloadLength} bytes but only {data.Length - payloadStart} follow the header.");

    // Every item costs at least three bits (flag, run, value) and expands to at most MaxRun values,
    // so a payload this short cannot hold the picture its header claims; refuse before allocating.
    var symbolCount = IwcCodec.SymbolCount(header.Width, header.Height, header.Levels);
    if (symbolCount > (long)header.PayloadLength * 8 / 3 * IwcSplayCoder.MaxRun + IwcSplayCoder.MaxRun)
      throw new InvalidDataException($"IWC payload of {header.PayloadLength} bytes cannot expand to the {symbolCount} coefficients of a {header.Width}x{header.Height} picture.");

    var symbols = IwcSplayCoder.Decode(data.Slice(payloadStart, header.PayloadLength), (int)symbolCount);
    var pixels = IwcCodec.Decode(header, symbols);

    return new() {
      Width = header.Width,
      Height = header.Height,
      Levels = header.Levels,
      Quality = header.Quality,
      PixelData = pixels,
    };
  }
}
