using System;

namespace FileFormat.Core;

/// <summary>Controls detection ordering; lower values are checked first. Default is 100. Use 999 for false-positive-prone formats like PCX.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FormatDetectionPriorityAttribute(int priority) : Attribute {
  public int Priority { get; } = priority;
}
