using System;

namespace FileFormat.Core;

/// <summary>Reads an array of N items, where N comes from another field or is a fixed constant.
/// <para>Example: <c>[Repeat(nameof(EntryCount))] WadEntry[] Entries</c> — reads EntryCount entries.</para>
/// <para>Example: <c>[Repeat(16)] short[] Palette</c> — reads exactly 16 shorts.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RepeatAttribute : Attribute {
  /// <summary>Name of the field whose value determines the repeat count.</summary>
  public string? CountField { get; }

  /// <summary>Fixed repeat count (when not driven by another field).</summary>
  public int FixedCount { get; }

  /// <summary>Repeat N times, where N comes from the named field.</summary>
  public RepeatAttribute(string countField) => CountField = countField;

  /// <summary>Repeat exactly N times.</summary>
  public RepeatAttribute(int fixedCount) => FixedCount = fixedCount;
}

/// <summary>Reads items until a sentinel value is encountered. The sentinel is consumed but not included.
/// <para>Example: <c>[RepeatUntil(0u)] uint[] PageOffsets</c> — reads uint32s until a zero is found.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RepeatUntilAttribute(object sentinel) : Attribute {
  public object Sentinel { get; } = sentinel;
}

/// <summary>Reads items until the end of the source buffer.
/// <para>Example: <c>[RepeatEos] ChunkHeader[] Chunks</c> — reads all remaining data as chunks.</para></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RepeatEosAttribute : Attribute { }
