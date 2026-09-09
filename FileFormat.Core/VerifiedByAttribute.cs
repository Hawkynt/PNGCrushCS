using System;

namespace FileFormat.Core;

/// <summary>
/// Names the tools outside this repository that have read what this format's writer produces.
/// </summary>
/// <remarks>
/// Put this on the format type — the same place <c>[FormatMagicBytes]</c> and
/// <c>[FormatMimeType]</c> go — and the registry generator carries it into the entry, which is what
/// the support tables print. It is a claim about the writer and only about the writer: our own
/// reader agreeing with our own writer is what this exists to stop being mistaken for evidence, so
/// this repository's own code is never a value here.
/// <para/>
/// The claim is not free. A format naming an oracle has to have a test that actually runs that tool
/// over bytes this writer produced, and there is a fixture that fails when it does not — otherwise
/// the column would be decoration, and a decorative column is worse than an empty one because it
/// reads like evidence.
/// <para/>
/// Absent means <see cref="ConformanceOracle.None"/>: nothing but our own reader has ever looked at
/// this writer's output. That is the ordinary case here and the honest thing to print.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class VerifiedByAttribute(params ConformanceOracle[] oracles) : Attribute {

  /// <summary>The tools that have read this writer's output.</summary>
  public ConformanceOracle[] Oracles { get; } = oracles;
}
