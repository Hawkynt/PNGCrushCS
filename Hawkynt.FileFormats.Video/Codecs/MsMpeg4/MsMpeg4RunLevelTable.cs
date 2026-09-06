using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// One of the six run-level tables, together with the two bounds its escape forms are measured
/// against.
/// </summary>
/// <remarks>
/// A row of the table stands for a triple: whether this is the block's last coefficient, how many zero
/// coefficients precede it, and its magnitude. A picture chooses one of three tables for luminance and
/// one of three for everything else, and version 3 says which in its picture header where versions 1
/// and 2 always use the middle one.
/// <para/>
/// The two derived bounds are what the first and second escape forms mean. The first says "this
/// magnitude, plus the largest the table can state for that run"; the second says "this run, plus the
/// largest the table can state for that magnitude". Both are properties of the table and neither is
/// transmitted, so they are computed from the rows here rather than written out beside them — two
/// statements of the same hundred-odd rows could disagree, and the disagreement would show up as a
/// coefficient in the wrong place rather than as anything that looks like a fault.
/// </remarks>
internal sealed class MsMpeg4RunLevelTable {

  /// <summary>The longest run and the largest magnitude the escapes' bounds are indexed over.</summary>
  private const int _MAX_RUN = 64;

  private const int _MAX_LEVEL = 64;

  private readonly int[] _run;
  private readonly int[] _level;
  private readonly int[][] _largestLevel;
  private readonly int[][] _largestRun;
  private readonly int[][] _firstOfRun;
  private readonly int[] _code;
  private readonly int[] _length;

  internal MsMpeg4RunLevelTable(string name, int lastIndex, int[] codes, int[] lengths, int[] runs, int[] levels) {
    ArgumentNullException.ThrowIfNull(runs);
    ArgumentNullException.ThrowIfNull(levels);

    this.Name = name;
    this.LastIndex = lastIndex;
    this.EscapeIndex = runs.Length;
    this._run = runs;
    this._level = levels;
    this._code = codes;
    this._length = lengths;
    this.Codes = MsMpeg4VlcTable.FromCodes(name, codes, lengths);

    this._largestLevel = [new int[_MAX_RUN + 1], new int[_MAX_RUN + 1]];
    this._largestRun = [new int[_MAX_LEVEL + 1], new int[_MAX_LEVEL + 1]];
    this._firstOfRun = [new int[_MAX_RUN + 1], new int[_MAX_RUN + 1]];
    Array.Fill(this._firstOfRun[0], this.EscapeIndex);
    Array.Fill(this._firstOfRun[1], this.EscapeIndex);

    for (var i = 0; i < runs.Length; ++i) {
      var last = i >= lastIndex ? 1 : 0;
      var run = runs[i];
      var level = levels[i];

      if (this._firstOfRun[last][run] == this.EscapeIndex)
        this._firstOfRun[last][run] = i;

      if (level > this._largestLevel[last][run])
        this._largestLevel[last][run] = level;

      if (run > this._largestRun[last][level])
        this._largestRun[last][level] = run;
    }
  }

  /// <summary>What to call the table in a refusal.</summary>
  internal string Name { get; }

  /// <summary>The first row that states a last coefficient; every row below it states a non-last one.</summary>
  internal int LastIndex { get; }

  /// <summary>The row that is not a triple at all but the escape into one of three other forms.</summary>
  internal int EscapeIndex { get; }

  /// <summary>The codewords, whose value is a row of this table.</summary>
  internal MsMpeg4VlcTable Codes { get; }

  /// <summary>The codeword of one row, right-aligned in <see cref="LengthOf"/> bits.</summary>
  internal int CodeOf(int index) => this._code[index];

  /// <summary>How many bits that codeword occupies.</summary>
  internal int LengthOf(int index) => this._length[index];

  /// <summary>Whether a row states the last coefficient of its block.</summary>
  internal bool IsLast(int index) => index >= this.LastIndex;

  /// <summary>How many zero coefficients a row puts before its own.</summary>
  internal int RunOf(int index) => this._run[index];

  /// <summary>The magnitude a row states.</summary>
  internal int LevelOf(int index) => this._level[index];

  /// <summary>The largest magnitude the table can state for one run, which the first escape adds to.</summary>
  internal int LargestLevel(bool last, int run) => this._largestLevel[last ? 1 : 0][run];

  /// <summary>The longest run the table can state for one magnitude, which the second escape adds to.</summary>
  internal int LargestRun(bool last, int level) => this._largestRun[last ? 1 : 0][level];

  /// <summary>
  /// The row that states one triple, or <see cref="EscapeIndex"/> where the table has no row for it.
  /// </summary>
  /// <remarks>
  /// The rows of one run sit together in increasing magnitude, so the row for a triple is the first
  /// row of its run plus its magnitude less one — which is why the table can be searched by arithmetic
  /// rather than by looking. A magnitude past the largest the table states for that run has no row and
  /// is where the escape forms begin.
  /// </remarks>
  internal int IndexOf(bool last, int run, int level) {
    if ((uint)run > _MAX_RUN || (uint)level > _MAX_LEVEL || level > this._largestLevel[last ? 1 : 0][run])
      return this.EscapeIndex;

    return this._firstOfRun[last ? 1 : 0][run] + level - 1;
  }
}
