using System;

namespace FileFormat.Jbig2.Codec;

/// <summary>Context model for JBIG2 arithmetic coding. Manages arrays of probability
/// estimation state indices (I) and MPS (Most Probable Symbol) values per context.</summary>
internal sealed class Jbig2ContextModel {

  /// <summary>A single arithmetic coding context with mutable state index and MPS.</summary>
  internal sealed class Context {
    /// <summary>Index into the QE probability estimation table (0-112).</summary>
    internal int I;

    /// <summary>Most Probable Symbol value (0 or 1).</summary>
    internal int Mps;
  }

  private readonly Context[] _contexts;

  /// <summary>Creates a context model with the specified number of contexts, all initialized to state 0, MPS=0.</summary>
  internal Jbig2ContextModel(int size) {
    _contexts = new Context[size];
    for (var i = 0; i < size; ++i)
      _contexts[i] = new Context();
  }

  /// <summary>Gets the context at the specified index.</summary>
  internal Context this[int index] => _contexts[index];

  /// <summary>Number of contexts in this model.</summary>
  internal int Count => _contexts.Length;

  /// <summary>Resets all contexts to initial state (I=0, MPS=0).</summary>
  internal void Reset() {
    for (var i = 0; i < _contexts.Length; ++i) {
      _contexts[i].I = 0;
      _contexts[i].Mps = 0;
    }
  }

  // ---- Standard context sizes for different JBIG2 region types ----

  /// <summary>Generic region template 0: 16-pixel context = 2^16 = 65536 contexts.</summary>
  internal const int GenericTemplate0Size = 1 << 16;

  /// <summary>Generic region template 1: 13-pixel context = 2^13 = 8192 contexts.</summary>
  internal const int GenericTemplate1Size = 1 << 13;

  /// <summary>Generic region template 2: 10-pixel context = 2^10 = 1024 contexts.</summary>
  internal const int GenericTemplate2Size = 1 << 10;

  /// <summary>Generic region template 3: 10-pixel context = 2^10 = 1024 contexts.</summary>
  internal const int GenericTemplate3Size = 1 << 10;

  /// <summary>Refinement template 0: 13-pixel context = 2^13 = 8192 contexts.</summary>
  internal const int RefinementTemplate0Size = 1 << 13;

  /// <summary>Refinement template 1: 10-pixel context = 2^10 = 1024 contexts.</summary>
  internal const int RefinementTemplate1Size = 1 << 10;

  /// <summary>Integer arithmetic coding contexts (512 states for prefix tree).</summary>
  internal const int IntegerSize = 512;
}
