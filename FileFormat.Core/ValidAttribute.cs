using System;

namespace FileFormat.Core;

/// <summary>Asserts that a field matches an expected value after reading.
/// The generator emits a check in <c>ReadFrom</c> that throws <see cref="System.IO.InvalidDataException"/> on mismatch.
/// <para>For byte[] magic: <c>[Valid(0x89, 0x50, 0x4E, 0x47)]</c> or <c>[Valid("qoif")]</c> (ASCII) or <c>[Valid("BM", StringEncoding.Unicode)]</c></para>
/// <para>For scalars: <c>[Valid(42)]</c>, <c>[Valid((byte)0x1F)]</c></para>
/// When applied to a <c>byte[]</c> field without an explicit size, the size is inferred from the expected value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidAttribute : Attribute {

  /// <summary>Validate a byte[] field against expected raw bytes: <c>[Valid(0x89, 0x50, 0x4E, 0x47)]</c></summary>
  public ValidAttribute(params byte[] expected) => ExpectedBytes = expected;

  /// <summary>Validate a byte[] field against an ASCII string: <c>[Valid("qoif")]</c></summary>
  public ValidAttribute(string expected) {
    ExpectedString = expected;
    Encoding = StringEncoding.Ascii;
  }

  /// <summary>Validate a byte[] field against an encoded string: <c>[Valid("BM", StringEncoding.Unicode)]</c></summary>
  public ValidAttribute(string expected, StringEncoding encoding) {
    ExpectedString = expected;
    Encoding = encoding;
  }

  /// <summary>Validate a scalar field against an exact value: <c>[Valid(42)]</c></summary>
  public ValidAttribute(object expected) => ExpectedScalar = expected;

  /// <summary>Raw bytes for magic validation.</summary>
  public byte[]? ExpectedBytes { get; }

  /// <summary>String for magic validation (encoded per <see cref="Encoding"/>).</summary>
  public string? ExpectedString { get; }

  /// <summary>Encoding for string-based magic validation.</summary>
  public StringEncoding Encoding { get; }

  /// <summary>Scalar value for numeric field validation.</summary>
  public object? ExpectedScalar { get; }
}

/// <summary>Asserts that a field's value falls within an inclusive range after reading.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidRangeAttribute(object min, object max) : Attribute {
  public object Min { get; } = min;
  public object Max { get; } = max;
}

/// <summary>Asserts that a field's value matches one of the allowed values after reading.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidAnyOfAttribute(params object[] allowed) : Attribute {
  public object[] Allowed { get; } = allowed;
}
