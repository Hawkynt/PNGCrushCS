using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Fits;

/// <summary>Assembles FITS file bytes from pixel data.</summary>
public static class FitsWriter {

  private const int _CARD_LENGTH = 80;
  private const int _BLOCK_SIZE = 2880;

  public static byte[] ToBytes(FitsFile file) {
    var channels = file.Channels is 3 or 4 ? file.Channels : 1;
    var expected = checked(file.Width * file.Height * channels * FitsFile.BytesPerSample(file.Bitpix));
    var pixels = file.PixelData ?? [];
    if (pixels.Length < expected)
      throw new InvalidDataException(
        $"FITS image declares {file.Width}x{file.Height}x{channels} at BITPIX {(int)file.Bitpix}, requiring {expected} bytes, but has {pixels.Length}.");

    return _Assemble(pixels, file.Width, file.Height, channels, file.Bitpix, file.Keywords ?? Array.Empty<FitsKeyword>());
  }

  private static byte[] _Assemble(
    byte[] pixelData,
    int width,
    int height,
    int channels,
    FitsBitpix bitpix,
    IReadOnlyList<FitsKeyword> keywords
  ) {
    if (width < 1 || height < 1)
      throw new InvalidDataException($"FITS dimensions must be positive; got {width}x{height}.");

    using var ms = new MemoryStream();

    // Build header cards. A conventional colour image is a three- or four-plane cube whose third
    // axis is the channel; NAXIS1 remains the fastest-varying x coordinate as FITS requires.
    var cards = new List<string>();
    cards.Add(_FormatCard("SIMPLE", "T", "conforms to FITS standard"));
    cards.Add(_FormatCard("BITPIX", ((int)bitpix).ToString(), "bits per sample"));
    cards.Add(_FormatCard("NAXIS", channels > 1 ? "3" : "2", "number of axes"));
    cards.Add(_FormatCard("NAXIS1", width.ToString(), "width"));
    cards.Add(_FormatCard("NAXIS2", height.ToString(), "height"));
    if (channels > 1)
      cards.Add(_FormatCard("NAXIS3", channels.ToString(), "colour planes"));

    // Add custom keywords (skip mandatory ones we already wrote)
    var mandatory = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "SIMPLE", "BITPIX", "NAXIS", "NAXIS1", "NAXIS2", "NAXIS3", "END"
    };
    for (var i = 0; i < keywords.Count; ++i) {
      var kw = keywords[i];
      if (!mandatory.Contains(kw.Name))
        cards.Add(_FormatCard(kw.Name, kw.Value, kw.Comment));
    }

    // END card
    cards.Add("END".PadRight(_CARD_LENGTH));

    // Write header blocks
    var headerBytes = new byte[_PadToBlock(cards.Count * _CARD_LENGTH)];
    // Fill with spaces (FITS header padding)
    for (var i = 0; i < headerBytes.Length; ++i)
      headerBytes[i] = (byte)' ';

    for (var i = 0; i < cards.Count; ++i) {
      var cardBytes = Encoding.ASCII.GetBytes(cards[i]);
      cardBytes.AsSpan(0, Math.Min(cardBytes.Length, _CARD_LENGTH)).CopyTo(headerBytes.AsSpan(i * _CARD_LENGTH));
    }

    ms.Write(headerBytes, 0, headerBytes.Length);

    var dataLength = checked(width * height * channels * FitsFile.BytesPerSample(bitpix));
    if (dataLength > 0)
      ms.Write(pixelData, 0, dataLength);

    // Pad data to 2880-byte boundary with zeros
    var dataRemainder = dataLength % _BLOCK_SIZE;
    if (dataRemainder > 0) {
      var padding = new byte[_BLOCK_SIZE - dataRemainder];
      ms.Write(padding, 0, padding.Length);
    }

    return ms.ToArray();
  }

  private static string _FormatCard(string name, string? value, string? comment) {
    var sb = new StringBuilder();
    sb.Append(name.PadRight(8)[..8]);

    if (value != null) {
      sb.Append("= ");
      sb.Append(value.PadLeft(20));
      if (comment != null) {
        sb.Append(" / ");
        sb.Append(comment);
      }
    }

    var result = sb.ToString();
    if (result.Length < _CARD_LENGTH)
      result = result.PadRight(_CARD_LENGTH);
    else if (result.Length > _CARD_LENGTH)
      result = result[.._CARD_LENGTH];

    return result;
  }

  private static int _PadToBlock(int length) {
    var remainder = length % _BLOCK_SIZE;
    return remainder == 0 ? length : length + _BLOCK_SIZE - remainder;
  }
}
