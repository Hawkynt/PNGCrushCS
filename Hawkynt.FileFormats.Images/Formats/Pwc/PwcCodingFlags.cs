using System;

namespace FileFormat.Pwc;

/// <summary>The option bits stored in the first byte of a PWC coding block.</summary>
[Flags]
public enum PwcCodingFlags : byte {

  /// <summary>Default coding: corner guesses on, list decision on.</summary>
  None = 0,

  /// <summary>Every pixel is coded by prediction (8-bit greyscale-like pictures); no edges or guesses.</summary>
  Gray = 0x01,

  /// <summary>Skip the above-left / above-right colour guesses (the encoder's <c>-noq2</c>).</summary>
  NoCornerGuesses = 0x02,

  /// <summary>Skip the "is it in the guess list at all?" decision (the encoder's default below 256 colours).</summary>
  NoListDecision = 0x04,
}
