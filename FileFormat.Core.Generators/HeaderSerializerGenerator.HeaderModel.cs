namespace FileFormat.Core.Generators;

public sealed partial class HeaderSerializerGenerator {
  private sealed class HeaderModel(
    string ns,
    string name,
    string accessibility,
    bool isReadOnly,
    FieldModel[] fields,
    int structSize,
    bool hasGaps,
    bool useInitSyntax,
    byte fillByte,
    int declaredStructSize = -1,
    string classEndianness = "Little",
    bool isSequential = false) {
    public string Namespace { get; } = ns;
    public string Name { get; } = name;
    public string Accessibility { get; } = accessibility;
    public bool IsReadOnly { get; } = isReadOnly;
    public FieldModel[] Fields { get; } = fields;
    public int StructSize { get; } = structSize;
    public bool HasGaps { get; } = hasGaps;
    public bool UseInitSyntax { get; } = useInitSyntax;
    public byte FillByte { get; } = fillByte;
    /// <summary>-1 if not declared via [StructSize].</summary>
    public int DeclaredStructSize { get; } = declaredStructSize;
    public string ClassEndianness { get; } = classEndianness;
    public bool IsSequential { get; } = isSequential;
  }
}
