namespace FileFormat.Core.Generators;

public sealed partial class HeaderSerializerGenerator {
  private sealed class FieldModel(
    string name,
    string typeName,
    int offset,
    int size,
    string endianness,
    string endianFieldName,
    int arrayLength,
    int bitOffset,
    int bitCount,
    bool isEnum,
    string underlyingEnumType,
    bool isSubStruct,
    int endianComputeValue,
    int asciiEncoding,
    ValidationModel validation = null,
    bool isSequential = false,
    string stringEncoding = null,
    bool isNullTermString = false,
    int fieldOffset = -1) {
    public string Name { get; } = name;
    public string TypeName { get; } = typeName;
    public int Offset { get; } = offset;
    public int Size { get; } = size;
    public string Endianness { get; } = endianness;
    public string EndianFieldName { get; } = endianFieldName;
    public int ArrayLength { get; } = arrayLength;
    public int BitOffset { get; } = bitOffset;
    public int BitCount { get; } = bitCount;
    public bool IsEnum { get; } = isEnum;
    public string UnderlyingEnumType { get; } = underlyingEnumType;
    public bool IsSubStruct { get; } = isSubStruct;
    public int EndianComputeValue { get; } = endianComputeValue;
    public int AsciiEncoding { get; } = asciiEncoding;
    public ValidationModel Validation { get; } = validation;
    /// <summary>True if this field uses [SeqField] (sequential positioning).</summary>
    public bool IsSequential { get; } = isSequential;
    /// <summary>Encoding name for [StringField] or [NullTermString] (e.g. "ASCII", "UTF-8", "Latin1"). Null if not a string field.</summary>
    public string? StringEncoding { get; } = stringEncoding;
    /// <summary>True if this is a [NullTermString] field (variable-length, reads until 0x00).</summary>
    public bool IsNullTermString { get; } = isNullTermString;
    /// <summary>[FieldOffset(N)] — jump cursor to absolute position N before this field. -1 = no offset override.</summary>
    public int FieldOffset { get; } = fieldOffset;
    /// <summary>[TypeOverride(WireType.X)] — wire format override. 0 = Native (no override).</summary>
    public int WireType { get; set; }

    // Phase 3/4: Conditional, SizedBy, Repeat
    /// <summary>[If(field, op, value)] — conditional inclusion.</summary>
    public IfModel? Condition { get; set; }
    /// <summary>[SizedBy(field)] — dynamic size from another field.</summary>
    public string? SizedByField { get; set; }
    /// <summary>[Repeat(N)] or [Repeat(field)] — array repetition.</summary>
    public RepeatModel? Repeat { get; set; }
  }

  private sealed class IfModel {
    public string FieldName { get; set; }
    public int Op { get; set; } // Op enum value
    public string Value { get; set; }
  }

  private sealed class RepeatModel {
    /// <summary>Fixed count, or -1 if driven by a field.</summary>
    public int FixedCount { get; set; } = -1;
    /// <summary>Name of the field whose value is the count.</summary>
    public string? CountField { get; set; }
  }

  private sealed class ValidationModel {
    /// <summary>[Valid(expected)] — exact scalar match.</summary>
    public string? ValidExact { get; set; }
    /// <summary>[Valid(bytes)] — exact byte array match.</summary>
    public byte[]? ValidBytes { get; set; }
    /// <summary>[Valid("magic")] — exact string match (converted to bytes via encoding).</summary>
    public string? ValidString { get; set; }
    /// <summary>Encoding for ValidString (e.g. "ASCII", "Unicode").</summary>
    public string? ValidStringEncoding { get; set; }
    /// <summary>[ValidRange(min, max)] — inclusive range.</summary>
    public string? ValidMin { get; set; }
    public string? ValidMax { get; set; }
    /// <summary>[ValidAnyOf(v1, v2, ...)] — whitelist.</summary>
    public string[]? ValidAnyOf { get; set; }

    /// <summary>Byte length of the expected magic. Used to infer byte[] field size.</summary>
    public int MagicLength {
      get {
        if (ValidBytes != null) return ValidBytes.Length;
        if (ValidString == null) return 0;
        var isWide = ValidStringEncoding is "Unicode" or "UnicodeBE";
        return ValidString.Length * (isWide ? 2 : 1);
      }
    }
  }
}
