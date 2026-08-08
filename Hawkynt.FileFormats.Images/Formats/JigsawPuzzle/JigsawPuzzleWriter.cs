using System;

namespace FileFormat.JigsawPuzzle;

/// <summary>Assembles a jigsaw puzzle picture: the bitmap, under the two letters these open with.</summary>
/// <remarks>
/// What follows the bitmap is the puzzle — which pieces there are, who drew the picture and what it
/// shows — and none of that is derivable from a picture. So this writes the bitmap and whatever
/// puzzle came with the file it was read from, and nothing invented in between.
/// </remarks>
public static class JigsawPuzzleWriter {

  public static byte[] ToBytes(JigsawPuzzleFile file) {
    var embedded = file.Embedded ?? [];
    var puzzle = file.Puzzle ?? [];

    var result = new byte[embedded.Length + puzzle.Length];
    embedded.CopyTo(result.AsSpan());
    puzzle.CopyTo(result.AsSpan(embedded.Length));

    if (result.Length >= JigsawPuzzleFile.Magic.Length)
      JigsawPuzzleFile.Magic.CopyTo(result);

    return result;
  }
}
