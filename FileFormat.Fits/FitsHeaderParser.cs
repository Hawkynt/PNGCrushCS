using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Fits;

/// <summary>Parses FITS 80-character keyword cards into <see cref="FitsKeyword"/> objects.</summary>
internal static class FitsHeaderParser {

  private const int _CARD_LENGTH = 80;
  private const int _BLOCK_SIZE = 2880;
  private const int _CARDS_PER_BLOCK = _BLOCK_SIZE / _CARD_LENGTH; // 36

  /// <summary>Parses all header blocks starting at the given offset and returns keywords plus the total header byte length.</summary>
  internal static (List<FitsKeyword> Keywords, int HeaderLength) Parse(byte[] data) {
    var keywords = new List<FitsKeyword>();
    var offset = 0;
    var endFound = false;

    while (!endFound && offset < data.Length) {
      var blockEnd = Math.Min(offset + _BLOCK_SIZE, data.Length);
      for (var cardStart = offset; cardStart < blockEnd; cardStart += _CARD_LENGTH) {
        if (cardStart + _CARD_LENGTH > data.Length)
          break;

        var card = Encoding.ASCII.GetString(data, cardStart, _CARD_LENGTH);
        var keyword = _ParseCard(card);
        if (keyword.Name == "END") {
          endFound = true;
          break;
        }

        keywords.Add(keyword);
      }

      offset += _BLOCK_SIZE;
    }

    return (keywords, offset);
  }

  private static FitsKeyword _ParseCard(string card) {
    var name = card[..8].TrimEnd();

    if (name.Length == 0 || card.Length < 10 || card[8] != '=' || card[9] != ' ')
      return new(name, null, null);

    var valueComment = card[10..];
    string? value;
    string? comment;

    if (valueComment.TrimStart().StartsWith('\'')) {
      // String value: find the matching closing quote
      var trimmed = valueComment.TrimStart();
      var quoteStart = valueComment.IndexOf('\'');
      var searchFrom = quoteStart + 1;
      var quoteEnd = -1;

      while (searchFrom < valueComment.Length) {
        var nextQuote = valueComment.IndexOf('\'', searchFrom);
        if (nextQuote < 0) {
          quoteEnd = valueComment.Length;
          break;
        }

        // Check for escaped quote ('')
        if (nextQuote + 1 < valueComment.Length && valueComment[nextQuote + 1] == '\'') {
          searchFrom = nextQuote + 2;
          continue;
        }

        quoteEnd = nextQuote;
        break;
      }

      if (quoteEnd < 0)
        quoteEnd = valueComment.Length;

      value = valueComment[(quoteStart + 1)..quoteEnd].Replace("''", "'").TrimEnd();

      var afterValue = quoteEnd + 1 < valueComment.Length ? valueComment[(quoteEnd + 1)..] : "";
      var slashPos = afterValue.IndexOf('/');
      comment = slashPos >= 0 ? afterValue[(slashPos + 1)..].Trim() : null;
    } else {
      // Numeric, boolean, or blank value
      var slashPos = valueComment.IndexOf('/');
      if (slashPos >= 0) {
        value = valueComment[..slashPos].Trim();
        comment = valueComment[(slashPos + 1)..].Trim();
      } else {
        value = valueComment.Trim();
        comment = null;
      }
    }

    if (string.IsNullOrEmpty(value))
      value = null;
    if (string.IsNullOrEmpty(comment))
      comment = null;

    return new(name, value, comment);
  }

  internal static int GetIntValue(IReadOnlyList<FitsKeyword> keywords, string name) {
    for (var i = 0; i < keywords.Count; ++i)
      if (keywords[i].Name == name && keywords[i].Value != null && int.TryParse(keywords[i].Value, out var v))
        return v;

    throw new InvalidOperationException($"Required FITS keyword '{name}' not found or invalid.");
  }

  internal static FitsBitpix GetBitpix(IReadOnlyList<FitsKeyword> keywords) {
    var value = GetIntValue(keywords, "BITPIX");
    return value switch {
      8 => FitsBitpix.UInt8,
      16 => FitsBitpix.Int16,
      32 => FitsBitpix.Int32,
      -32 => FitsBitpix.Float32,
      -64 => FitsBitpix.Float64,
      _ => throw new InvalidOperationException($"Unsupported BITPIX value: {value}.")
    };
  }
}
