namespace FileFormat.Ktx;

/// <summary>A key-value pair stored in the KTX key-value data section.</summary>
public sealed class KtxKeyValue {
  public string Key { get; init; } = string.Empty;
  public byte[] Value { get; init; } = [];
}
