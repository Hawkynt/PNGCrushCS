using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FileFormat.Core.Generators;

[Generator]
public sealed partial class HeaderSerializerGenerator : IIncrementalGenerator {

  private const string _GENERATE_SERIALIZER_ATTRIBUTE_NAME = "FileFormat.Core.GenerateSerializerAttribute";
  private const string _HEADER_FIELD_ATTRIBUTE_NAME = "FileFormat.Core.HeaderFieldAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var structs = context.SyntaxProvider.CreateSyntaxProvider(
      static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } and (StructDeclarationSyntax or RecordDeclarationSyntax),
      static (ctx, _) => _TryExtractModel(ctx)
    ).Where(static m => m != null);

    context.RegisterSourceOutput(structs, static (spc, model) => _Generate(spc, model));
  }

  private static HeaderModel _TryExtractModel(GeneratorSyntaxContext ctx) {
    var tds = (TypeDeclarationSyntax)ctx.Node;

    if (ctx.SemanticModel.GetDeclaredSymbol(tds) is not INamedTypeSymbol symbol)
      return null;

    AttributeData serializerAttr = null;
    foreach (var a in symbol.GetAttributes())
      if (a.AttributeClass?.ToDisplayString() == _GENERATE_SERIALIZER_ATTRIBUTE_NAME) {
        serializerAttr = a;
        break;
      }

    if (serializerAttr == null)
      return null;

    // Check if the type has a primary constructor (parameter list in the declaration)
    var hasPrimaryConstructor = tds is RecordDeclarationSyntax { ParameterList.Parameters.Count: > 0 };

    return _ExtractModelFromSymbol(symbol, !hasPrimaryConstructor, serializerAttr);
  }

  private static HeaderModel _ExtractModelFromSymbol(INamedTypeSymbol symbol, bool useInitSyntax, AttributeData serializerAttr) {
    byte fillByte = 0;
    if (serializerAttr != null)
      foreach (var named in serializerAttr.NamedArguments)
        if (named.Key == "FillByte") {
          fillByte = (byte)named.Value.Value;
          break;
        }

    var fields = new List<FieldModel>();

    foreach (var member in symbol.GetMembers()) {
      if (member is not IPropertySymbol prop)
        continue;

      var attr = _FindAttribute(prop, _HEADER_FIELD_ATTRIBUTE_NAME);
      if (attr == null)
        continue;

      var args = attr.ConstructorArguments;
      if (args.Length < 2)
        continue;

      var offset = (int)args[0].Value;
      var size = (int)args[1].Value;
      var endianness = "Little";
      string endianFieldName = null;
      var arrayLength = 0;
      var bitOffset = -1;
      var bitCount = 0;
      var endianComputeValue = int.MinValue;
      var asciiEncoding = 0;

      foreach (var named in attr.NamedArguments) {
        switch (named.Key) {
          case "Endianness":
            endianness = named.Value.Value?.ToString() == "1" ? "Big" : "Little";
            break;
          case "EndianFieldName":
            endianFieldName = named.Value.Value as string;
            break;
          case "ArrayLength":
            arrayLength = (int)named.Value.Value;
            break;
          case "BitOffset":
            bitOffset = (int)named.Value.Value;
            break;
          case "BitCount":
            bitCount = (int)named.Value.Value;
            break;
          case "EndianComputeValue":
            endianComputeValue = (int)named.Value.Value;
            break;
          case "AsciiEncoding":
            asciiEncoding = (int)named.Value.Value;
            break;
        }
      }

      var typeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      var isEnum = prop.Type.TypeKind == TypeKind.Enum;
      string underlyingEnumType = null;
      if (isEnum) {
        var namedType = (INamedTypeSymbol)prop.Type;
        underlyingEnumType = namedType.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      }

      var isSubStruct = !isEnum
                        && prop.Type is { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None }
                        && typeName != "global::System.Half"
                        && !typeName.EndsWith("[]");

      fields.Add(new FieldModel(
        prop.Name,
        typeName,
        offset,
        size,
        endianness,
        endianFieldName,
        arrayLength,
        bitOffset,
        bitCount,
        isEnum,
        underlyingEnumType,
        isSubStruct,
        endianComputeValue,
        asciiEncoding
      ));
    }

    if (fields.Count == 0)
      return null;

    fields.Sort((a, b) => a.Offset.CompareTo(b.Offset));

    var ns = symbol.ContainingNamespace.IsGlobalNamespace
      ? null
      : symbol.ContainingNamespace.ToDisplayString();

    string accessibility;
    switch (symbol.DeclaredAccessibility) {
      case Accessibility.Public:
        accessibility = "public";
        break;
      case Accessibility.Internal:
        accessibility = "internal";
        break;
      case Accessibility.Private:
        accessibility = "private";
        break;
      case Accessibility.Protected:
        accessibility = "protected";
        break;
      default:
        accessibility = "internal";
        break;
    }

    var isReadOnly = symbol.IsReadOnly;
    var fieldEnd = _ComputeStructSize(fields);
    var fillerEnd = _ComputeFillerEnd(symbol);
    var structSize = fieldEnd > fillerEnd ? fieldEnd : fillerEnd;
    var hasGaps = _HasGaps(fields, structSize);

    return new HeaderModel(
      ns,
      symbol.Name,
      accessibility,
      isReadOnly,
      fields.ToArray(),
      structSize,
      hasGaps,
      useInitSyntax,
      fillByte
    );
  }

  private static int _ComputeStructSize(List<FieldModel> fields) {
    var max = 0;
    foreach (var f in fields) {
      var end = f.Offset + f.Size;
      if (end > max)
        max = end;
    }
    return max;
  }

  private static int _ComputeFillerEnd(INamedTypeSymbol symbol) {
    var max = 0;
    foreach (var attr in symbol.GetAttributes()) {
      if (attr.AttributeClass?.ToDisplayString() != "FileFormat.Core.HeaderFillerAttribute")
        continue;

      var args = attr.ConstructorArguments;
      if (args.Length < 3)
        continue;

      var offset = (int)args[1].Value;
      var size = (int)args[2].Value;
      var end = offset + size;
      if (end > max)
        max = end;
    }
    return max;
  }

  private static bool _HasGaps(List<FieldModel> fields, int structSize) {
    if (structSize == 0)
      return false;

    var totalFieldBytes = 0;
    foreach (var f in fields)
      totalFieldBytes += f.Size;

    if (totalFieldBytes >= structSize)
      return false;

    // Fields don't cover entire struct — there are padding/reserved gaps
    return true;
  }

  private static AttributeData _FindAttribute(ISymbol symbol, string fullName) {
    foreach (var attr in symbol.GetAttributes())
      if (attr.AttributeClass?.ToDisplayString() == fullName)
        return attr;

    return null;
  }

  private static void _Generate(SourceProductionContext ctx, HeaderModel model) {
    if (model == null)
      return;

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Buffers.Binary;");
    sb.AppendLine("using System.Text;");
    sb.AppendLine();

    if (model.Namespace != null) {
      sb.Append("namespace ").Append(model.Namespace).AppendLine(";");
      sb.AppendLine();
    }

    sb.Append(model.Accessibility);
    if (model.IsReadOnly)
      sb.Append(" readonly");
    sb.Append(" partial record struct ").Append(model.Name).Append(" : global::FileFormat.Core.IBinarySerializable<").Append(model.Name).AppendLine("> {");
    sb.AppendLine();

    sb.Append("  static int global::FileFormat.Core.IBinarySerializable<").Append(model.Name).Append(">.SerializedSize => ").Append(model.StructSize).AppendLine(";");
    sb.AppendLine();

    _GenerateReadFrom(sb, model);
    sb.AppendLine();
    _GenerateWriteTo(sb, model);

    sb.AppendLine("}");

    ctx.AddSource(model.Name + ".g.cs", sb.ToString());
  }

  private static void _GenerateReadFrom(StringBuilder sb, HeaderModel model) {
    var hasComputedEndian = _HasComputedEndianFields(model);
    var useInit = model.UseInitSyntax;
    var useBlock = hasComputedEndian;
    var opener = useInit ? "new() {" : "new(";
    var closer = useInit ? "};" : ");";

    if (useBlock) {
      sb.Append("  public static ").Append(model.Name).AppendLine(" ReadFrom(global::System.ReadOnlySpan<byte> source) {");
      _EmitEndianComputeLocalsRead(sb, model);
      sb.Append("    return ").AppendLine(opener);
    } else
      sb.Append("  public static ").Append(model.Name).Append(" ReadFrom(global::System.ReadOnlySpan<byte> source) => ").AppendLine(opener);

    for (var i = 0; i < model.Fields.Length; ++i) {
      var f = model.Fields[i];
      sb.Append("    ");
      if (useInit)
        sb.Append(f.Name).Append(" = ");
      _EmitReadExpression(sb, f, model.FillByte);
      if (i < model.Fields.Length - 1)
        sb.AppendLine(",");
      else
        sb.AppendLine();
    }

    if (useBlock) {
      sb.Append("    ").AppendLine(closer);
      sb.AppendLine("  }");
    } else
      sb.Append("  ").AppendLine(closer);
  }

  private static bool _HasComputedEndianFields(HeaderModel model) {
    foreach (var f in model.Fields)
      if (f.EndianFieldName != null && f.EndianComputeValue != int.MinValue)
        return true;

    return false;
  }

  private static FieldModel _FindFieldByName(HeaderModel model, string name) {
    foreach (var f in model.Fields)
      if (f.Name == name)
        return f;

    return null;
  }

  private static void _EmitEndianComputeLocalsRead(StringBuilder sb, HeaderModel model) {
    var emitted = new HashSet<string>();
    foreach (var f in model.Fields) {
      if (f.EndianFieldName == null || f.EndianComputeValue == int.MinValue)
        continue;

      if (!emitted.Add(f.EndianFieldName))
        continue;

      var endianField = _FindFieldByName(model, f.EndianFieldName);
      if (endianField == null)
        continue;

      sb.Append("    var _isBE = ");
      var refEndianSuffix = endianField.Endianness == "Big" ? "BigEndian" : "LittleEndian";
      switch (endianField.Size) {
        case 1:
          sb.Append("source[").Append(endianField.Offset).Append("] == (byte)").Append(f.EndianComputeValue);
          break;
        case 2:
          sb.Append("global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16").Append(refEndianSuffix).Append("(source[").Append(endianField.Offset).Append("..]) == ").Append(f.EndianComputeValue);
          break;
        default:
          sb.Append("global::System.Buffers.Binary.BinaryPrimitives.ReadInt32").Append(refEndianSuffix).Append("(source[").Append(endianField.Offset).Append("..]) == ").Append(f.EndianComputeValue);
          break;
      }
      sb.AppendLine(";");
    }
  }

  private static void _EmitEndianComputeLocalsWrite(StringBuilder sb, HeaderModel model) {
    var emitted = new HashSet<string>();
    foreach (var f in model.Fields) {
      if (f.EndianFieldName == null || f.EndianComputeValue == int.MinValue)
        continue;

      if (!emitted.Add(f.EndianFieldName))
        continue;

      sb.Append("    var _isBE = this.").Append(f.EndianFieldName).Append(" == ").Append(f.EndianComputeValue).AppendLine(";");
    }
  }

  private static string _GetEndianVarName(FieldModel f)
    => f.EndianComputeValue != int.MinValue ? "_isBE" : f.EndianFieldName;

  private static void _EmitReadExpression(StringBuilder sb, FieldModel f, byte fillByte) {
    // ASCII-decimal-encoded numeric fields
    if (f.AsciiEncoding == 1) {
      if (f.IsEnum)
        sb.Append("(").Append(f.TypeName).Append(")");
      sb.Append("int.Parse(global::System.Text.Encoding.ASCII.GetString(source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append(")).Trim())");
      return;
    }

    // byte array fields
    if (f.TypeName is "byte[]" or "global::System.Byte[]") {
      sb.Append("source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append(").ToArray()");
      return;
    }

    // string fields
    if (f.TypeName is "string" or "global::System.String") {
      sb.Append("global::System.Text.Encoding.ASCII.GetString(source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("))");
      if (fillByte != 0)
        sb.Append(".TrimEnd('\\0', (char)").Append(fillByte).Append(")");
      else
        sb.Append(".TrimEnd('\\0')");
      return;
    }

    // fixed arrays of primitives
    if (f.ArrayLength > 0) {
      _EmitArrayRead(sb, f);
      return;
    }

    // bitfield extraction
    if (f.BitOffset >= 0) {
      _EmitBitfieldRead(sb, f);
      return;
    }

    // embedded sub-struct types
    if (f.IsSubStruct) {
      sb.Append(f.TypeName).Append(".ReadFrom(source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("))");
      return;
    }

    // enum types
    if (f.IsEnum)
      sb.Append("(").Append(f.TypeName).Append(")(");

    _EmitPrimitiveRead(sb, f);

    if (f.IsEnum)
      sb.Append(")");
  }

  private static void _EmitPrimitiveRead(StringBuilder sb, FieldModel f) {
    var endian = f.Endianness;
    var hasRuntimeEndian = f.EndianFieldName != null;
    var endianVar = hasRuntimeEndian ? _GetEndianVarName(f) : null;

    switch (f.Size) {
      case 1:
        sb.Append("source[").Append(f.Offset).Append("]");
        break;
      case 2:
        if (hasRuntimeEndian) {
          sb.Append(endianVar).Append(" ? ");
          _EmitBinaryRead(sb, f, "BigEndian");
          sb.Append(" : ");
          _EmitBinaryRead(sb, f, "LittleEndian");
        } else
          _EmitBinaryRead(sb, f, endian == "Big" ? "BigEndian" : "LittleEndian");
        break;
      case 3:
        if (endian == "Big")
          sb.Append("(source[").Append(f.Offset).Append("] << 16) | (source[").Append(f.Offset + 1).Append("] << 8) | source[").Append(f.Offset + 2).Append("]");
        else
          sb.Append("source[").Append(f.Offset).Append("] | (source[").Append(f.Offset + 1).Append("] << 8) | (source[").Append(f.Offset + 2).Append("] << 16)");
        break;
      case 4:
        if (hasRuntimeEndian) {
          sb.Append(endianVar).Append(" ? ");
          _EmitBinaryRead(sb, f, "BigEndian");
          sb.Append(" : ");
          _EmitBinaryRead(sb, f, "LittleEndian");
        } else
          _EmitBinaryRead(sb, f, endian == "Big" ? "BigEndian" : "LittleEndian");
        break;
      case 8:
        if (hasRuntimeEndian) {
          sb.Append(endianVar).Append(" ? ");
          _EmitBinaryRead(sb, f, "BigEndian");
          sb.Append(" : ");
          _EmitBinaryRead(sb, f, "LittleEndian");
        } else
          _EmitBinaryRead(sb, f, endian == "Big" ? "BigEndian" : "LittleEndian");
        break;
      default:
        sb.Append("default /* unsupported size ").Append(f.Size).Append(" */");
        break;
    }
  }

  private static void _EmitBinaryRead(StringBuilder sb, FieldModel f, string endianSuffix) {
    var baseType = f.IsEnum ? (f.UnderlyingEnumType ?? "global::System.Int32") : f.TypeName;
    var method = _GetBinaryPrimitivesMethodName(baseType, f.Size, "Read", endianSuffix);
    sb.Append("global::System.Buffers.Binary.BinaryPrimitives.").Append(method).Append("(source[").Append(f.Offset).Append("..])");
  }

  private static string _GetBinaryPrimitivesMethodName(string typeName, int size, string prefix, string endianSuffix) {
    string coreType;
    switch (typeName) {
      case "short":
      case "global::System.Int16":
        coreType = "Int16";
        break;
      case "ushort":
      case "global::System.UInt16":
        coreType = "UInt16";
        break;
      case "int":
      case "global::System.Int32":
        coreType = "Int32";
        break;
      case "uint":
      case "global::System.UInt32":
        coreType = "UInt32";
        break;
      case "long":
      case "global::System.Int64":
        coreType = "Int64";
        break;
      case "ulong":
      case "global::System.UInt64":
        coreType = "UInt64";
        break;
      case "float":
      case "global::System.Single":
        coreType = size == 2 ? "Half" : "Single";
        break;
      case "double":
      case "global::System.Double":
        coreType = "Double";
        break;
      default:
        switch (size) {
          case 2:
            coreType = "Int16";
            break;
          case 4:
            coreType = "Int32";
            break;
          case 8:
            coreType = "Int64";
            break;
          default:
            coreType = "Int32";
            break;
        }
        break;
    }

    return prefix + coreType + endianSuffix;
  }

  private static void _EmitArrayRead(StringBuilder sb, FieldModel f) {
    var elementSize = f.Size / f.ArrayLength;

    if (elementSize == 1) {
      sb.Append("source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append(").ToArray()");
      return;
    }

    var elemTypeName = _GetElementTypeName(f, elementSize);
    sb.Append("new ").Append(elemTypeName).Append("[] { ");
    for (var e = 0; e < f.ArrayLength; ++e) {
      if (e > 0)
        sb.Append(", ");

      var elemOffset = f.Offset + e * elementSize;
      var endianSuffix = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";
      var method = _GetBinaryPrimitivesMethodName(elemTypeName, elementSize, "Read", endianSuffix);
      sb.Append("global::System.Buffers.Binary.BinaryPrimitives.").Append(method).Append("(source[").Append(elemOffset).Append("..])");
    }
    sb.Append(" }");
  }

  private static string _GetElementTypeName(FieldModel f, int elementSize) {
    if (f.TypeName.EndsWith("[]"))
      return f.TypeName.Substring(0, f.TypeName.Length - 2);

    switch (elementSize) {
      case 1: return "byte";
      case 2: return "short";
      case 4: return "int";
      case 8: return "long";
      default: return "byte";
    }
  }

  private static void _EmitBitfieldRead(StringBuilder sb, FieldModel f) {
    var mask = (1 << f.BitCount) - 1;

    if (f.IsEnum)
      sb.Append("(").Append(f.TypeName).Append(")(");

    switch (f.Size) {
      case 1:
        sb.Append("(source[").Append(f.Offset).Append("] >> ").Append(f.BitOffset).Append(") & 0x").Append(mask.ToString("X"));
        break;
      case 2:
        var endian2 = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";
        sb.Append("(global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16").Append(endian2).Append("(source[").Append(f.Offset).Append("..]) >> ").Append(f.BitOffset).Append(") & 0x").Append(mask.ToString("X"));
        break;
      case 4:
        var endian4 = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";
        sb.Append("(int)((global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32").Append(endian4).Append("(source[").Append(f.Offset).Append("..]) >> ").Append(f.BitOffset).Append(") & 0x").Append(((uint)mask).ToString("X")).Append("u)");
        break;
      default:
        sb.Append("0 /* unsupported bitfield size */");
        break;
    }

    if (f.IsEnum)
      sb.Append(")");
  }

  private static void _GenerateWriteTo(StringBuilder sb, HeaderModel model) {
    sb.AppendLine("  public void WriteTo(global::System.Span<byte> destination) {");

    if (model.HasGaps) {
      if (model.FillByte != 0)
        sb.Append("    destination[..").Append(model.StructSize).Append("].Fill(").Append(model.FillByte).AppendLine(");");
      else
        sb.Append("    destination[..").Append(model.StructSize).AppendLine("].Clear();");
    }

    if (_HasComputedEndianFields(model))
      _EmitEndianComputeLocalsWrite(sb, model);

    // Group bitfield fields by (Offset, Size) for combined writes
    var bitfieldGroups = new Dictionary<(int Offset, int Size), List<FieldModel>>();
    var emittedBitfieldOffsets = new HashSet<(int, int)>();

    foreach (var f in model.Fields) {
      if (f.BitOffset < 0)
        continue;

      var key = (f.Offset, f.Size);
      if (!bitfieldGroups.ContainsKey(key))
        bitfieldGroups[key] = new List<FieldModel>();
      bitfieldGroups[key].Add(f);
    }

    foreach (var f in model.Fields) {
      if (f.BitOffset >= 0) {
        var key = (f.Offset, f.Size);
        if (!emittedBitfieldOffsets.Add(key))
          continue;

        sb.Append("    ");
        _EmitBitfieldWrite(sb, bitfieldGroups[key]);
        sb.AppendLine();
        continue;
      }

      sb.Append("    ");
      _EmitWriteStatement(sb, f, model.HasGaps);
      sb.AppendLine();
    }

    sb.AppendLine("  }");
  }

  private static void _EmitBitfieldWrite(StringBuilder sb, List<FieldModel> group) {
    var first = group[0];
    var containerSize = first.Size;

    // Build the OR expression: (field1 & mask1) << shift1 | (field2 & mask2) << shift2 | ...
    var expr = new StringBuilder();
    for (var i = 0; i < group.Count; ++i) {
      if (i > 0)
        expr.Append(" | ");

      var f = group[i];
      var mask = (1 << f.BitCount) - 1;
      var value = f.IsEnum ? "(int)this." + f.Name : "this." + f.Name;

      if (f.BitOffset == 0)
        expr.Append("(").Append(value).Append(" & 0x").Append(mask.ToString("X")).Append(")");
      else
        expr.Append("((").Append(value).Append(" & 0x").Append(mask.ToString("X")).Append(") << ").Append(f.BitOffset).Append(")");
    }

    switch (containerSize) {
      case 1:
        sb.Append("destination[").Append(first.Offset).Append("] = (byte)(").Append(expr).Append(");");
        break;
      case 2: {
        var endian = first.Endianness == "Big" ? "BigEndian" : "LittleEndian";
        sb.Append("global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16").Append(endian).Append("(destination[").Append(first.Offset).Append("..], (ushort)(").Append(expr).Append("));");
        break;
      }
      case 4: {
        var endian = first.Endianness == "Big" ? "BigEndian" : "LittleEndian";
        sb.Append("global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32").Append(endian).Append("(destination[").Append(first.Offset).Append("..], (uint)(").Append(expr).Append("));");
        break;
      }
      default:
        sb.Append("/* unsupported bitfield container size ").Append(containerSize).Append(" */");
        break;
    }
  }

  private static void _EmitWriteStatement(StringBuilder sb, FieldModel f, bool hasGaps) {
    // ASCII-decimal-encoded numeric fields
    if (f.AsciiEncoding == 1) {
      var value = f.IsEnum ? "((int)this." + f.Name + ")" : "this." + f.Name;
      sb.Append("global::System.Text.Encoding.ASCII.GetBytes(").Append(value).Append(".ToString(\"D").Append(f.Size).Append("\")).AsSpan(0, ").Append(f.Size).Append(").CopyTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
      return;
    }

    var hasRuntimeEndian = f.EndianFieldName != null;
    var endianVar = hasRuntimeEndian ? _GetEndianVarName(f) : null;

    switch (f.TypeName) {
      case "byte[]":
      case "global::System.Byte[]":
        sb.Append("this.").Append(f.Name).Append(".AsSpan().CopyTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
        return;
      case "string":
      case "global::System.String":
        if (!hasGaps)
          sb.Append("destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).AppendLine(").Clear();");
        if (!hasGaps)
          sb.Append("    ");
        sb.Append("global::System.Text.Encoding.ASCII.GetBytes(this.").Append(f.Name).Append(" ?? \"\").AsSpan().CopyTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
        return;
    }

    if (f.ArrayLength > 0) {
      _EmitArrayWrite(sb, f);
      return;
    }

    if (f.IsSubStruct) {
      sb.Append("this.").Append(f.Name).Append(".WriteTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
      return;
    }

    switch (f.Size) {
      case 1:
        if (f.IsEnum)
          sb.Append("destination[").Append(f.Offset).Append("] = (byte)this.").Append(f.Name).Append(";");
        else
          sb.Append("destination[").Append(f.Offset).Append("] = this.").Append(f.Name).Append(";");
        break;
      case 2: {
        var value = f.IsEnum ? "(" + _GetWriteCastType(f, 2) + ")this." + f.Name : "this." + f.Name;
        if (hasRuntimeEndian) {
          sb.Append("if (").Append(endianVar).Append(") ");
          sb.Append(_GetBinaryWriteCall(f, value, "BigEndian"));
          sb.Append(" else ");
          sb.Append(_GetBinaryWriteCall(f, value, "LittleEndian"));
        } else
          sb.Append(_GetBinaryWriteCall(f, value, f.Endianness == "Big" ? "BigEndian" : "LittleEndian"));
        break;
      }
      case 3: {
        var varName = "this." + f.Name;
        if (f.Endianness == "Big") {
          sb.Append("destination[").Append(f.Offset).Append("] = (byte)(").Append(varName).Append(" >> 16); ");
          sb.Append("destination[").Append(f.Offset + 1).Append("] = (byte)(").Append(varName).Append(" >> 8); ");
          sb.Append("destination[").Append(f.Offset + 2).Append("] = (byte)").Append(varName).Append(";");
        } else {
          sb.Append("destination[").Append(f.Offset).Append("] = (byte)").Append(varName).Append("; ");
          sb.Append("destination[").Append(f.Offset + 1).Append("] = (byte)(").Append(varName).Append(" >> 8); ");
          sb.Append("destination[").Append(f.Offset + 2).Append("] = (byte)(").Append(varName).Append(" >> 16);");
        }
        break;
      }
      case 4: {
        var value = f.IsEnum ? "(" + _GetWriteCastType(f, 4) + ")this." + f.Name : "this." + f.Name;
        if (hasRuntimeEndian) {
          sb.Append("if (").Append(endianVar).Append(") ");
          sb.Append(_GetBinaryWriteCall(f, value, "BigEndian"));
          sb.Append(" else ");
          sb.Append(_GetBinaryWriteCall(f, value, "LittleEndian"));
        } else
          sb.Append(_GetBinaryWriteCall(f, value, f.Endianness == "Big" ? "BigEndian" : "LittleEndian"));
        break;
      }
      case 8: {
        var value = f.IsEnum ? "(" + _GetWriteCastType(f, 8) + ")this." + f.Name : "this." + f.Name;
        if (hasRuntimeEndian) {
          sb.Append("if (").Append(endianVar).Append(") ");
          sb.Append(_GetBinaryWriteCall(f, value, "BigEndian"));
          sb.Append(" else ");
          sb.Append(_GetBinaryWriteCall(f, value, "LittleEndian"));
        } else
          sb.Append(_GetBinaryWriteCall(f, value, f.Endianness == "Big" ? "BigEndian" : "LittleEndian"));
        break;
      }
      default:
        sb.Append("/* unsupported size ").Append(f.Size).Append(" for field ").Append(f.Name).Append(" */");
        break;
    }
  }

  private static string _GetWriteCastType(FieldModel f, int size) {
    if (f.UnderlyingEnumType != null)
      return f.UnderlyingEnumType;

    switch (size) {
      case 1: return "byte";
      case 2: return "short";
      case 4: return "int";
      case 8: return "long";
      default: return "int";
    }
  }

  private static string _GetBinaryWriteCall(FieldModel f, string value, string endianSuffix) {
    var baseType = f.IsEnum ? (f.UnderlyingEnumType ?? "global::System.Int32") : f.TypeName;
    var method = _GetBinaryPrimitivesMethodName(baseType, f.Size, "Write", endianSuffix);
    return "global::System.Buffers.Binary.BinaryPrimitives." + method + "(destination[" + f.Offset + "..], " + value + ");";
  }

  private static void _EmitArrayWrite(StringBuilder sb, FieldModel f) {
    var elementSize = f.Size / f.ArrayLength;

    if (elementSize == 1) {
      sb.Append("if (this.").Append(f.Name).Append(" != null) this.").Append(f.Name).Append(".AsSpan().CopyTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
      return;
    }

    var endianSuffix = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";
    var elemTypeName = _GetElementTypeName(f, elementSize);
    var method = _GetBinaryPrimitivesMethodName(elemTypeName, elementSize, "Write", endianSuffix);

    sb.Append("if (this.").Append(f.Name).Append(" != null) for (var _i = 0; _i < ").Append(f.ArrayLength).Append("; ++_i) ");
    sb.Append("global::System.Buffers.Binary.BinaryPrimitives.").Append(method).Append("(destination[(").Append(f.Offset).Append(" + _i * ").Append(elementSize).Append(")..], this.").Append(f.Name).Append("[_i]);");
  }
}