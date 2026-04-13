using System;

namespace FileFormat.Core;

/// <summary>Declares an additional image format enum value for the source generator, used for formats without an <see cref="IImageFormatReader{TSelf}"/> implementation (e.g., detection-only formats from external libraries).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AdditionalImageFormatAttribute(string formatId) : Attribute {
  public string FormatId { get; } = formatId;
}
