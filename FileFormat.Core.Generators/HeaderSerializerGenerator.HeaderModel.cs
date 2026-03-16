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
    byte fillByte) {
    public string Namespace { get; } = ns;
    public string Name { get; } = name;
    public string Accessibility { get; } = accessibility;
    public bool IsReadOnly { get; } = isReadOnly;
    public FieldModel[] Fields { get; } = fields;
    public int StructSize { get; } = structSize;
    public bool HasGaps { get; } = hasGaps;
    public bool UseInitSyntax { get; } = useInitSyntax;
    public byte FillByte { get; } = fillByte;
  }
}
