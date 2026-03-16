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
    int asciiEncoding) {
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
  }
}
