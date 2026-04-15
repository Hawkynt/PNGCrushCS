using System;

namespace FileFormat.Core;

/// <summary>Marks a <c>byte[]</c> field as uninterpreted raw bytes. Equivalent to a <c>byte[]</c> type detected automatically,
/// but makes intent explicit and ensures the field is treated as a binary blob (not a string or encoded value).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RawBytesAttribute : Attribute { }
