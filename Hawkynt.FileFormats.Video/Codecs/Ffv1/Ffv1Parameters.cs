using System;
using System.IO;

namespace FileFormat.Codecs.Ffv1;

/// <summary>What a stream's samples are and how they are coded (RFC 9043 §4.2).</summary>
/// <remarks>
/// The same fields in the same order for every version; where they live is what changes. Versions 0
/// and 1 put them inside every keyframe, so a frame describes itself and a container needs to carry
/// nothing. Version 3 moves them into a configuration record the container carries once, which is
/// what lets a decoder be set up before a frame arrives and what the record's checksum protects.
/// </remarks>
internal sealed class Ffv1Parameters {

  private const int _MAX_QUANT_TABLE_SETS = 8;
  private const int _MAX_CONTEXT_INPUTS = 5;

  internal int Version { get; private set; }
  internal int MicroVersion { get; private set; }
  internal int CoderType { get; private set; }
  internal int ColourSpaceType { get; private set; }
  internal int BitsPerRawSample { get; private set; } = 8;
  internal bool ChromaPlanes { get; private set; }
  internal int ChromaHorizontalShift { get; private set; }
  internal int ChromaVerticalShift { get; private set; }
  internal bool ExtraPlane { get; private set; }
  internal int HorizontalSlices { get; private set; } = 1;
  internal int VerticalSlices { get; private set; } = 1;
  internal int QuantTableSetCount { get; private set; } = 1;
  internal int ErrorCorrection { get; private set; }
  internal bool IntraOnly { get; private set; }

  /// <summary>The five tables of each set, each 256 entries indexed by a masked sample difference.</summary>
  internal int[][][] QuantTables { get; } = new int[_MAX_QUANT_TABLE_SETS][][];

  /// <summary>How many contexts each set has, which is how many state arrays a plane using it needs.</summary>
  internal int[] ContextCount { get; } = new int[_MAX_QUANT_TABLE_SETS];

  /// <summary>The states each context of each set starts at, where the stream states them.</summary>
  internal byte[][][]? InitialStates { get; private set; }

  /// <summary>The state transition differences a coder type of two carries.</summary>
  internal int[] StateTransitionDelta { get; } = new int[256];

  internal bool HasStateTransitionDelta { get; private set; }

  /// <summary>How many planes a frame is made of, in the order they are coded.</summary>
  internal int PlaneCount => 1 + (this.ChromaPlanes ? 2 : 0) + (this.ExtraPlane ? 1 : 0);

  /// <summary>Bits a coded sample difference is taken modulo, which the colour transform widens by one.</summary>
  internal int SampleBits => this.BitsPerRawSample + (this.ColourSpaceType == 1 ? 1 : 0);

  /// <summary>
  /// How many table set indices a slice header states, which is one per kind of plane.
  /// </summary>
  /// <remarks>
  /// The chrominance slot is there even for a stream that has no chrominance, for every version this
  /// reads. Early writers stored it regardless and the specification keeps it so their files stay
  /// readable, which also means an extra plane's index is always the third.
  /// </remarks>
  internal int QuantTableSetIndexCount => 1 + (this.ChromaPlanes || this.Version <= 3 ? 1 : 0) + (this.ExtraPlane ? 1 : 0);

  /// <summary>Reads the parameters, whichever place they are in.</summary>
  internal static Ffv1Parameters Read(Ffv1RangeCoder coder, byte[] states, bool fromConfigurationRecord) {
    var parameters = new Ffv1Parameters { Version = coder.Symbol(states, false) };

    if (parameters.Version >= 3)
      parameters.MicroVersion = coder.Symbol(states, false);

    if (parameters.Version is not (0 or 1 or 3))
      throw new NotSupportedException(
        $"The stream states FFV1 version {parameters.Version}. Versions 0, 1 and 3 are the ones RFC 9043 describes and the ones read here; version 2 was never finished and anything higher was written after this was.");

    if (parameters.Version >= 3 && !fromConfigurationRecord)
      throw new InvalidDataException("A version 3 stream states its parameters inside a frame, where the container is meant to carry them.");

    if (parameters.Version <= 1 && fromConfigurationRecord)
      throw new InvalidDataException($"A version {parameters.Version} stream carries a configuration record, which only version 3 has.");

    parameters.CoderType = coder.Symbol(states, false);
    if (parameters.CoderType > 2)
      throw new NotSupportedException(
        $"The stream states coder type {parameters.CoderType}, where 0 is Golomb-Rice, 1 is the range coder with the default state transitions and 2 the range coder with its own.");

    if (parameters.CoderType > 1) {
      parameters.HasStateTransitionDelta = true;
      for (var i = 1; i < 256; ++i)
        parameters.StateTransitionDelta[i] = coder.Symbol(states, true);
    }

    parameters.ColourSpaceType = coder.Symbol(states, false);
    if (parameters.ColourSpaceType > 1)
      throw new NotSupportedException(
        $"The stream states colour space {parameters.ColourSpaceType}, where 0 is luminance and chrominance and 1 is colour through the JPEG 2000 reversible transform. Nothing else is described.");

    if (parameters.Version >= 1)
      parameters.BitsPerRawSample = coder.Symbol(states, false);

    if (parameters.BitsPerRawSample == 0)
      parameters.BitsPerRawSample = 8;

    parameters.ChromaPlanes = coder.Get(states, 0) != 0;
    parameters.ChromaHorizontalShift = coder.Symbol(states, false);
    parameters.ChromaVerticalShift = coder.Symbol(states, false);
    parameters.ExtraPlane = coder.Get(states, 0) != 0;

    if (parameters.Version >= 3) {
      parameters.HorizontalSlices = coder.Symbol(states, false) + 1;
      parameters.VerticalSlices = coder.Symbol(states, false) + 1;
      parameters.QuantTableSetCount = coder.Symbol(states, false);
    }

    if (parameters.QuantTableSetCount is <= 0 or > _MAX_QUANT_TABLE_SETS)
      throw new InvalidDataException(
        $"The stream states {parameters.QuantTableSetCount} quantisation table set(s), where between one and {_MAX_QUANT_TABLE_SETS} is what a stream may have.");

    for (var i = 0; i < parameters.QuantTableSetCount; ++i)
      parameters._ReadQuantTableSet(coder, i);

    if (parameters.Version >= 3) {
      parameters._ReadInitialStates(coder, states);
      parameters.ErrorCorrection = coder.Symbol(states, false);
      parameters.IntraOnly = coder.Symbol(states, false) != 0;
    }

    if (parameters.ColourSpaceType == 1 && (!parameters.ChromaPlanes || parameters.ChromaHorizontalShift != 0 || parameters.ChromaVerticalShift != 0))
      throw new InvalidDataException(
        "The stream states the colour transform with subsampled or missing colour planes, which RFC 9043 says is outside it.");

    return parameters;
  }

  /// <summary>
  /// Reads one set of five quantisation tables (RFC 9043 §4.1).
  /// </summary>
  /// <remarks>
  /// Only the first half of each table is in the file, as the lengths of its runs of equal entries;
  /// the second half is the first with the sign turned round, because a difference of <i>-n</i>
  /// belongs in the mirror of the context a difference of <i>n</i> does. Each table's output is then
  /// multiplied by the product of the ranges of the tables before it, which packs all five into one
  /// number without any of them overlapping — a mixed-radix count rather than a sum of independent
  /// terms.
  /// </remarks>
  private void _ReadQuantTableSet(Ffv1RangeCoder coder, int set) {
    var tables = new int[_MAX_CONTEXT_INPUTS][];
    var scale = 1;

    for (var table = 0; table < _MAX_CONTEXT_INPUTS; ++table) {
      // Each of the five starts from states of its own rather than carrying on from the table
      // before it, which is what keeps a table's runs from being read against another's statistics.
      var tableStates = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
      Array.Fill(tableStates, (byte)128);

      var entries = new int[256];
      var value = 0;
      var k = 0;

      while (k < 128) {
        var length = coder.Symbol(tableStates, false) + 1;
        if (length <= 0 || k + length > 128)
          throw new InvalidDataException(
            $"A quantisation table states a run of {length} entries where {128 - k} are left of its first half.");

        for (var n = 0; n < length; ++n)
          entries[k++] = scale * value;

        ++value;
      }

      for (k = 1; k < 128; ++k)
        entries[256 - k] = -entries[k];

      entries[128] = -entries[127];

      tables[table] = entries;
      scale *= 2 * value - 1;
    }

    if (scale is < 1 or > 65535)
      throw new InvalidDataException($"A quantisation table set describes {(scale + 1) / 2} contexts, where 32768 is the most a stream may have.");

    this.QuantTables[set] = tables;
    this.ContextCount[set] = (scale + 1) / 2;
  }

  /// <summary>
  /// Reads the states each context starts at, which a stream may state instead of taking 128.
  /// </summary>
  /// <remarks>
  /// Stated as differences from the context before, so a stream whose contexts are alike costs
  /// almost nothing to describe. The first context's difference is from 128, which is where every
  /// state starts when a stream says nothing.
  /// </remarks>
  private void _ReadInitialStates(Ffv1RangeCoder coder, byte[] states) {
    byte[][][]? initial = null;

    // One set of coder states per position within a context, so that the difference in a context's
    // fifth state is read against what fifth states have been doing and not against all of them.
    var perPosition = new byte[Ffv1RangeCoder.CONTEXT_SIZE][];
    for (var k = 0; k < perPosition.Length; ++k) {
      perPosition[k] = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
      Array.Fill(perPosition[k], (byte)128);
    }

    for (var set = 0; set < this.QuantTableSetCount; ++set) {
      if (coder.Get(states, 0) == 0)
        continue;

      initial ??= new byte[_MAX_QUANT_TABLE_SETS][][];
      var contexts = new byte[this.ContextCount[set]][];
      for (var context = 0; context < contexts.Length; ++context) {
        contexts[context] = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
        for (var k = 0; k < Ffv1RangeCoder.CONTEXT_SIZE; ++k) {
          var previous = context > 0 ? contexts[context - 1][k] : 128;
          contexts[context][k] = (byte)((previous + coder.Symbol(perPosition[k], true)) & 0xFF);
        }
      }

      initial[set] = contexts;
    }

    this.InitialStates = initial;
  }

  /// <summary>
  /// Which of the three kinds of plane a plane is: luminance, chrominance, or the extra one.
  /// </summary>
  /// <remarks>
  /// The two chrominance planes are one kind and not two. That is what decides which quantisation
  /// table set they use, and — the part that is easy to miss — it also decides whose adaptive states
  /// they share: Cb and Cr code their samples against one set of contexts between them, not one
  /// each. A decoder that gave them a set apiece decodes a greyscale stream perfectly, decodes the
  /// luminance and the first chrominance plane of a colour one perfectly, and gets every sample of
  /// the second chrominance plane wrong.
  /// </remarks>
  internal int PlaneKindOf(int plane) => plane switch {
    0 => 0,
    1 or 2 when this.ChromaPlanes || this.ColourSpaceType == 1 => 1,
    _ => this.Version <= 3 || this.ChromaPlanes ? 2 : 1,
  };

  /// <summary>Which table set a plane's contexts come from (RFC 9043 §3.6).</summary>
  internal int TableSetIndexOf(int plane, int[] stated) => stated[this.PlaneKindOf(plane)];
}
