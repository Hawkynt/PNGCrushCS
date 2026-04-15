using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FileFormat.Core.Generators;

[Generator]
public sealed partial class HeaderSerializerGenerator : IIncrementalGenerator {

  private const string _GENERATE_SERIALIZER_ATTRIBUTE_NAME = "FileFormat.Core.GenerateSerializerAttribute";
  private const string _FIELD_ATTRIBUTE_NAME = "FileFormat.Core.FieldAttribute";
  private const string _ENDIAN_ATTRIBUTE_NAME = "FileFormat.Core.EndianAttribute";
  private const string _STRUCT_SIZE_ATTRIBUTE_NAME = "FileFormat.Core.StructSizeAttribute";
  private const string _FILLER_ATTRIBUTE_NAME = "FileFormat.Core.FillerAttribute";
  private const string _VALID_ATTRIBUTE_NAME = "FileFormat.Core.ValidAttribute";
  private const string _VALID_RANGE_ATTRIBUTE_NAME = "FileFormat.Core.ValidRangeAttribute";
  private const string _VALID_ANY_OF_ATTRIBUTE_NAME = "FileFormat.Core.ValidAnyOfAttribute";
  private const string _SEQ_FIELD_ATTRIBUTE_NAME = "FileFormat.Core.SeqFieldAttribute";
  private const string _STRING_ATTRIBUTE_NAME = "FileFormat.Core.StringAttribute";
  private const string _STRINGZ_ATTRIBUTE_NAME = "FileFormat.Core.StringZAttribute";
  private const string _FIELD_OFFSET_ATTRIBUTE_NAME = "FileFormat.Core.FieldOffsetAttribute";
  private const string _TYPE_OVERRIDE_ATTRIBUTE_NAME = "FileFormat.Core.TypeOverrideAttribute";
  private const string _IF_ATTRIBUTE_NAME = "FileFormat.Core.IfAttribute";
  private const string _SIZED_BY_ATTRIBUTE_NAME = "FileFormat.Core.SizedByAttribute";
  private const string _REPEAT_ATTRIBUTE_NAME = "FileFormat.Core.RepeatAttribute";
  private const string _REPEAT_UNTIL_ATTRIBUTE_NAME = "FileFormat.Core.RepeatUntilAttribute";
  private const string _REPEAT_EOS_ATTRIBUTE_NAME = "FileFormat.Core.RepeatEosAttribute";
  private const string _SWITCH_ON_ATTRIBUTE_NAME = "FileFormat.Core.SwitchOnAttribute";

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
    var modeExplicit = false;
    var isSequential = false;
    if (serializerAttr != null) {
      // Check constructor arg for LayoutMode.Sequential (value = 1)
      if (serializerAttr.ConstructorArguments.Length >= 1 && serializerAttr.ConstructorArguments[0].Value is int modeVal) {
        isSequential = modeVal == 1;
        modeExplicit = true;
      }

      foreach (var named in serializerAttr.NamedArguments)
        switch (named.Key) {
          case "FillByte": fillByte = (byte)named.Value.Value; break;
          case "Mode":
            if (named.Value.Value is int namedMode) {
              isSequential = namedMode == 1;
              modeExplicit = true;
            }
            break;
        }
    }

    // Read [Endian] class-level default
    var classEndianness = "Little";
    foreach (var a in symbol.GetAttributes())
      if (a.AttributeClass?.ToDisplayString() == _ENDIAN_ATTRIBUTE_NAME && a.ConstructorArguments.Length >= 1)
        classEndianness = a.ConstructorArguments[0].Value?.ToString() == "1" ? "Big" : "Little";

    // Read [StructSize] for compile-time assertion
    var declaredStructSize = -1;
    foreach (var a in symbol.GetAttributes())
      if (a.AttributeClass?.ToDisplayString() == _STRUCT_SIZE_ATTRIBUTE_NAME && a.ConstructorArguments.Length >= 1)
        declaredStructSize = (int)a.ConstructorArguments[0].Value;

    var fields = new List<FieldModel>();

    foreach (var member in symbol.GetMembers()) {
      if (member is not IPropertySymbol prop)
        continue;

      // Accept [HeaderField] (legacy), [Field] (new), [SeqField] (explicit sequential), or bare property (implicit sequential)
      var attr = _FindAttribute(prop, _FIELD_ATTRIBUTE_NAME);
      var seqAttr = _FindAttribute(prop, _SEQ_FIELD_ATTRIBUTE_NAME);

      if (attr == null && seqAttr == null) {
        // No explicit field attribute — candidate for implicit sequential inclusion.
        // Include ONLY if it's a compiler-synthesized property from a primary constructor parameter
        // (these have a matching constructor parameter with the same name).
        var isConstructorParam = false;
        if (!prop.IsStatic && !prop.IsImplicitlyDeclared) {
          foreach (var ctor in symbol.InstanceConstructors)
            foreach (var param in ctor.Parameters)
              if (param.Name == prop.Name) {
                isConstructorParam = true;
                break;
              }
        }

        var hasStringAttr = _FindAttribute(prop, _STRING_ATTRIBUTE_NAME) != null;
        var hasNullTermAttr = _FindAttribute(prop, _STRINGZ_ATTRIBUTE_NAME) != null;
        var hasValidAttr = _FindAttribute(prop, _VALID_ATTRIBUTE_NAME) != null
                        || _FindAttribute(prop, _VALID_RANGE_ATTRIBUTE_NAME) != null
                        || _FindAttribute(prop, _VALID_ANY_OF_ATTRIBUTE_NAME) != null;
        var hasDynAttr = _FindAttribute(prop, _IF_ATTRIBUTE_NAME) != null
                      || _FindAttribute(prop, _SIZED_BY_ATTRIBUTE_NAME) != null
                      || _FindAttribute(prop, _REPEAT_ATTRIBUTE_NAME) != null
                      || _FindAttribute(prop, _REPEAT_UNTIL_ATTRIBUTE_NAME) != null
                      || _FindAttribute(prop, _REPEAT_EOS_ATTRIBUTE_NAME) != null;

        if (!isConstructorParam && !hasStringAttr && !hasNullTermAttr && !hasValidAttr && !hasDynAttr)
          continue;
      }

      // Read [StringField] and [NullTermString] from the property
      string stringEncoding = null;
      var isNullTermString = false;
      var strFieldAttr = _FindAttribute(prop, _STRING_ATTRIBUTE_NAME);
      if (strFieldAttr != null)
        stringEncoding = _ParseStringEncoding(strFieldAttr);
      var nullTermAttr = _FindAttribute(prop, _STRINGZ_ATTRIBUTE_NAME);
      if (nullTermAttr != null) {
        isNullTermString = true;
        stringEncoding = _ParseStringEncoding(nullTermAttr);
      }

      int offset;
      int size;
      var isSeqField = attr == null; // no [Field]/[HeaderField] → sequential (implicit or explicit)

      if (isSeqField) {
        // Sequential field — offset is computed at codegen time, size from [SeqField] attribute or inferred from type
        offset = -1; // sentinel: sequential
        size = 0;
        if (seqAttr != null)
          foreach (var named in seqAttr.NamedArguments)
            if (named.Key == "Size" && named.Value.Value is int sz)
              size = sz;

        if (size == 0 && !isNullTermString) {
          size = _InferSizeFromType(prop.Type);
          // For array types with [Repeat], infer size from element type * count
          if (size == 0 && prop.Type is IArrayTypeSymbol arrayType) {
            var elemSize = _InferSizeFromType(arrayType.ElementType);
            var repAttr = _FindAttribute(prop, _REPEAT_ATTRIBUTE_NAME);
            if (repAttr != null && repAttr.ConstructorArguments.Length >= 1 && repAttr.ConstructorArguments[0].Value is int repCount)
              size = elemSize * repCount;
          }
        }
        // byte[] size from [Valid] magic is deferred until after validation extraction
      } else {
        var args = attr.ConstructorArguments;
        if (args.Length < 2)
          continue;

        offset = (int)args[0].Value;
        size = (int)args[1].Value;
      }

      var fieldEndianExplicit = false;
      var endianness = classEndianness; // inherit class-level default
      string endianFieldName = null;
      var arrayLength = 0;
      var bitOffset = -1;
      var bitCount = 0;
      var endianComputeValue = int.MinValue;
      var namedArgs = isSeqField
        ? (seqAttr?.NamedArguments ?? System.Collections.Immutable.ImmutableArray<KeyValuePair<string, TypedConstant>>.Empty)
        : attr.NamedArguments;
      foreach (var named in namedArgs) {
        switch (named.Key) {
          case "Endianness":
          case "Endian":
            var val = named.Value.Value;
            // [Field] uses sentinel (-1) for "not set"; [HeaderField] always sets a value
            if (val is int intVal && intVal >= 0) {
              endianness = intVal == 1 ? "Big" : "Little";
              fieldEndianExplicit = true;
            } else if (val != null) {
              endianness = val.ToString() == "1" ? "Big" : "Little";
              fieldEndianExplicit = true;
            }
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
          // AsciiEncoding removed — use [TypeOverride(WireType.DecimalString)] instead
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

      // Extract validation attributes
      ValidationModel validation = null;

      var validAttr = _FindAttribute(prop, _VALID_ATTRIBUTE_NAME);
      if (validAttr != null && validAttr.ConstructorArguments.Length >= 1) {
        var arg0 = validAttr.ConstructorArguments[0];

        if (arg0.Value is string magicStr) {
          // [Valid("qoif")] or [Valid("BM", StringEncoding.Unicode)]
          var enc = "ASCII";
          if (validAttr.ConstructorArguments.Length >= 2 && validAttr.ConstructorArguments[1].Value is int encVal) {
            switch (encVal) { case 1: enc = "UTF8"; break; case 2: enc = "Latin1"; break; case 3: enc = "Unicode"; break; case 4: enc = "UnicodeBE"; break; }
          }
          validation = new ValidationModel { ValidString = magicStr, ValidStringEncoding = enc };
        } else if (arg0.Kind == TypedConstantKind.Array) {
          // [Valid(new byte[] { 0x89, 0x50 })] or [Valid(0x89, 0x50)] (params byte[])
          var bytes = new List<byte>();
          foreach (var elem in arg0.Values)
            if (elem.Value is byte b) bytes.Add(b);
          if (bytes.Count > 0)
            validation = new ValidationModel { ValidBytes = bytes.ToArray() };
        } else {
          // Scalar: [Valid(42)], [Valid((byte)0x1F)]
          validation = new ValidationModel { ValidExact = _FormatConstant(arg0) };
        }
      }

      var validRangeAttr = _FindAttribute(prop, _VALID_RANGE_ATTRIBUTE_NAME);
      if (validRangeAttr != null && validRangeAttr.ConstructorArguments.Length >= 2) {
        validation ??= new ValidationModel();
        validation.ValidMin = _FormatConstant(validRangeAttr.ConstructorArguments[0]);
        validation.ValidMax = _FormatConstant(validRangeAttr.ConstructorArguments[1]);
      }

      var validAnyOfAttr = _FindAttribute(prop, _VALID_ANY_OF_ATTRIBUTE_NAME);
      if (validAnyOfAttr != null && validAnyOfAttr.ConstructorArguments.Length >= 1) {
        var arr = validAnyOfAttr.ConstructorArguments[0];
        if (arr.Kind == TypedConstantKind.Array) {
          var values = new List<string>();
          foreach (var v in arr.Values)
            values.Add(_FormatConstant(v));
          if (values.Count > 0) {
            validation ??= new ValidationModel();
            validation.ValidAnyOf = values.ToArray();
          }
        }
      }

      // Deferred: infer byte[] size from [Valid("magic")] or [Valid(bytes)] length
      if (isSeqField && size == 0 && !isNullTermString && validation != null && validation.MagicLength > 0)
        size = validation.MagicLength;

      // Extract [FieldOffset(N)] for cursor positioning
      var fieldOffset = -1;
      var fieldOffsetAttr = _FindAttribute(prop, _FIELD_OFFSET_ATTRIBUTE_NAME);
      if (fieldOffsetAttr != null && fieldOffsetAttr.ConstructorArguments.Length >= 1)
        fieldOffset = (int)fieldOffsetAttr.ConstructorArguments[0].Value;

      // Extract [TypeOverride(WireType.X)]
      var wireType = 0; // 0 = Native
      var typeOverrideAttr = _FindAttribute(prop, _TYPE_OVERRIDE_ATTRIBUTE_NAME);
      if (typeOverrideAttr != null && typeOverrideAttr.ConstructorArguments.Length >= 1 && typeOverrideAttr.ConstructorArguments[0].Value is int wt)
        wireType = wt;

      // Extract [If(field, op, value)]
      IfModel ifModel = null;
      var ifAttr = _FindAttribute(prop, _IF_ATTRIBUTE_NAME);
      if (ifAttr != null && ifAttr.ConstructorArguments.Length >= 3) {
        ifModel = new IfModel {
          FieldName = ifAttr.ConstructorArguments[0].Value as string ?? "",
          Op = ifAttr.ConstructorArguments[1].Value is int opVal ? opVal : 0,
          Value = _FormatConstant(ifAttr.ConstructorArguments[2])
        };
      }

      // Extract [SizedBy(field)]
      string sizedByField = null;
      var sizedByAttr = _FindAttribute(prop, _SIZED_BY_ATTRIBUTE_NAME);
      if (sizedByAttr != null && sizedByAttr.ConstructorArguments.Length >= 1)
        sizedByField = sizedByAttr.ConstructorArguments[0].Value as string;

      // Extract [Repeat(N)] or [Repeat(field)]
      RepeatModel repeatModel = null;
      var repeatAttr = _FindAttribute(prop, _REPEAT_ATTRIBUTE_NAME);
      if (repeatAttr != null && repeatAttr.ConstructorArguments.Length >= 1) {
        var arg = repeatAttr.ConstructorArguments[0];
        if (arg.Value is int fixedCount)
          repeatModel = new RepeatModel { FixedCount = fixedCount };
        else if (arg.Value is string countField)
          repeatModel = new RepeatModel { CountField = countField };
      }

      var fm = new FieldModel(
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
        0, // asciiEncoding removed
        validation,
        isSeqField,
        stringEncoding,
        isNullTermString,
        fieldOffset
      );
      fm.WireType = wireType;
      fm.Condition = ifModel;
      fm.SizedByField = sizedByField;
      fm.Repeat = repeatModel;
      fields.Add(fm);
    }

    if (fields.Count == 0)
      return null;

    // Auto-detect mode from field types if not explicitly specified
    if (!modeExplicit) {
      var hasFixed = false;
      var hasSeq = false;
      foreach (var f in fields) {
        if (f.IsSequential) hasSeq = true;
        else hasFixed = true;
      }
      isSequential = hasSeq && !hasFixed;
    }

    // Sort only fixed-layout fields by offset; sequential fields stay in declaration order
    if (!isSequential)
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
    var fieldEnd = _ComputeStructSize(fields, isSequential);
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
      fillByte,
      declaredStructSize,
      classEndianness,
      isSequential
    );
  }

  private static int _ComputeStructSize(List<FieldModel> fields, bool isSequential) {
    if (isSequential) {
      // Sequential: sum all fixed-size fields
      var total = 0;
      foreach (var f in fields)
        total += f.Size; // NullTermString fields have size 0, which is correct (variable-length)
      return total;
    }

    // Fixed-layout: max(offset + size)
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
      var name = attr.AttributeClass?.ToDisplayString();

      // [Filler(offset, size)]
      if (name == _FILLER_ATTRIBUTE_NAME && attr.ConstructorArguments.Length >= 2) {
        var offset = (int)attr.ConstructorArguments[0].Value;
        var size = (int)attr.ConstructorArguments[1].Value;
        var end = offset + size;
        if (end > max) max = end;
      }
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

  private static int _InferSizeFromType(ITypeSymbol type) {
    var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    switch (name) {
      case "byte":
      case "global::System.Byte":
      case "sbyte":
      case "global::System.SByte":
        return 1;
      case "short":
      case "global::System.Int16":
      case "ushort":
      case "global::System.UInt16":
        return 2;
      case "int":
      case "global::System.Int32":
      case "uint":
      case "global::System.UInt32":
      case "float":
      case "global::System.Single":
        return 4;
      case "long":
      case "global::System.Int64":
      case "ulong":
      case "global::System.UInt64":
      case "double":
      case "global::System.Double":
        return 8;
      default:
        // Enum — infer from underlying type
        if (type.TypeKind == TypeKind.Enum) {
          var underlying = ((INamedTypeSymbol)type).EnumUnderlyingType;
          if (underlying != null)
            return _InferSizeFromType(underlying);
        }
        return 0; // unknown — caller must provide explicit Size
    }
  }

  private static string _FormatConstant(TypedConstant tc) {
    if (tc.Value == null)
      return "null";

    switch (tc.Value) {
      case byte b: return $"(byte)0x{b:X2}";
      case sbyte sb: return $"(sbyte){sb}";
      case short s: return $"(short){s}";
      case ushort us: return $"{us}u";
      case int i: return i.ToString();
      case uint u: return $"{u}u";
      case long l: return $"{l}L";
      case ulong ul: return $"{ul}uL";
      case float f: return $"{f}f";
      case double d: return $"{d}d";
      case string str: return $"\"{str}\"";
      default: return tc.Value.ToString();
    }
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

    // Emit [StructSize] compile-time assertion if declared (fixed-layout only)
    if (!model.IsSequential && model.DeclaredStructSize >= 0 && model.DeclaredStructSize != model.StructSize) {
      ctx.ReportDiagnostic(Diagnostic.Create(
        new DiagnosticDescriptor(
          "FMTGEN001",
          "StructSize mismatch",
          "[StructSize({0})] declared on {1} but computed size from fields is {2}",
          "FileFormat.Core.Generators",
          DiagnosticSeverity.Error,
          true
        ),
        location: null,
        model.DeclaredStructSize, model.Name, model.StructSize
      ));
    }

    if (model.IsSequential) {
      _GenerateSeqReadFrom(sb, model);
      sb.AppendLine();
      _GenerateSeqWriteTo(sb, model);
    } else {
      _GenerateReadFrom(sb, model);
      sb.AppendLine();
      _GenerateWriteTo(sb, model);
    }

    sb.AppendLine();
    _GenerateFieldMap(sb, model);
    _EmitWireTypeHelpers(sb, model);

    sb.AppendLine("}");

    ctx.AddSource(model.Name + ".g.cs", sb.ToString());
  }

  private static bool _HasValidation(HeaderModel model) {
    foreach (var f in model.Fields)
      if (f.Validation != null)
        return true;
    return false;
  }

  private static void _GenerateReadFrom(StringBuilder sb, HeaderModel model) {
    var hasComputedEndian = _HasComputedEndianFields(model);
    var hasValidation = _HasValidation(model);
    var useInit = model.UseInitSyntax;
    var useBlock = hasComputedEndian || hasValidation;
    var opener = useInit ? "new() {" : "new(";
    var closer = useInit ? "};" : ");";

    if (useBlock) {
      sb.Append("  public static ").Append(model.Name).AppendLine(" ReadFrom(global::System.ReadOnlySpan<byte> source) {");
      if (hasComputedEndian)
        _EmitEndianComputeLocalsRead(sb, model);

      if (hasValidation) {
        // Use block syntax: read into var, validate, return
        sb.Append("    var _result = ").AppendLine(opener);
        for (var i = 0; i < model.Fields.Length; ++i) {
          var f = model.Fields[i];
          sb.Append("      ");
          if (useInit) sb.Append(f.Name).Append(" = ");
          _EmitReadExpression(sb, f, model.FillByte);
          sb.AppendLine(i < model.Fields.Length - 1 ? "," : "");
        }
        sb.Append("    ").AppendLine(closer);

        // Emit validation checks
        foreach (var f in model.Fields) {
          if (f.Validation == null) continue;
          _EmitValidation(sb, f);
        }

        sb.AppendLine("    return _result;");
      } else {
        sb.Append("    return ").AppendLine(opener);
        for (var i = 0; i < model.Fields.Length; ++i) {
          var f = model.Fields[i];
          sb.Append("    ");
          if (useInit) sb.Append(f.Name).Append(" = ");
          _EmitReadExpression(sb, f, model.FillByte);
          sb.AppendLine(i < model.Fields.Length - 1 ? "," : "");
        }
        sb.Append("    ").AppendLine(closer);
      }
      sb.AppendLine("  }");
    } else {
      sb.Append("  public static ").Append(model.Name).Append(" ReadFrom(global::System.ReadOnlySpan<byte> source) => ").AppendLine(opener);
      for (var i = 0; i < model.Fields.Length; ++i) {
        var f = model.Fields[i];
        sb.Append("    ");
        if (useInit) sb.Append(f.Name).Append(" = ");
        _EmitReadExpression(sb, f, model.FillByte);
        sb.AppendLine(i < model.Fields.Length - 1 ? "," : "");
      }
      sb.Append("  ").AppendLine(closer);
    }
  }

  private static void _EmitMagicValidation(StringBuilder sb, string accessor, string fieldName, ValidationModel v) {
    if (v.ValidString != null) {
      var enc = v.ValidStringEncoding ?? "ASCII";
      var isWide = enc is "Unicode" or "UnicodeBE";
      if (!isWide) {
        // ASCII/UTF8/Latin1 — use u8 string literal for zero-alloc comparison
        sb.Append("    if (!").Append(accessor).Append(".AsSpan().SequenceEqual(\"").Append(v.ValidString).Append("\"u8)) throw new global::System.IO.InvalidDataException(\"");
        sb.Append(fieldName).Append(": invalid magic, expected \\\"").Append(v.ValidString).AppendLine("\\\"\");");
      } else {
        // Unicode — encode at runtime
        var encExpr = _GetEncodingExpression(enc);
        sb.Append("    if (!").Append(accessor).Append(".AsSpan().SequenceEqual(").Append(encExpr).Append(".GetBytes(\"").Append(v.ValidString).Append("\"))) throw new global::System.IO.InvalidDataException(\"");
        sb.Append(fieldName).Append(": invalid magic, expected \\\"").Append(v.ValidString).AppendLine("\\\"\");");
      }
    } else if (v.ValidBytes != null) {
      // [Valid(new byte[] { 0x89, 0x50 })] — compare byte[] against expected bytes
      sb.Append("    if (!").Append(accessor).Append(".AsSpan().SequenceEqual(new byte[] { ");
      for (var i = 0; i < v.ValidBytes.Length; ++i) {
        if (i > 0) sb.Append(", ");
        sb.Append("0x").Append(v.ValidBytes[i].ToString("X2"));
      }
      sb.Append(" })) throw new global::System.IO.InvalidDataException(\"");
      sb.Append(fieldName).AppendLine(": invalid magic bytes\");");
    }
  }

  private static void _EmitScalarValidation(StringBuilder sb, string accessor, string fieldName, ValidationModel v) {
    if (v.ValidExact != null) {
      sb.Append("    if (").Append(accessor).Append(" != ").Append(v.ValidExact).Append(") throw new global::System.IO.InvalidDataException($\"");
      sb.Append(fieldName).Append(": expected ").Append(v.ValidExact).Append(" but got {").Append(accessor).AppendLine("}\");");
    }

    if (v.ValidMin != null && v.ValidMax != null) {
      sb.Append("    if (").Append(accessor).Append(" < ").Append(v.ValidMin).Append(" || ").Append(accessor).Append(" > ").Append(v.ValidMax).Append(") throw new global::System.IO.InvalidDataException($\"");
      sb.Append(fieldName).Append(": expected ").Append(v.ValidMin).Append("..").Append(v.ValidMax).Append(" but got {").Append(accessor).AppendLine("}\");");
    }

    if (v.ValidAnyOf != null && v.ValidAnyOf.Length > 0) {
      sb.Append("    if (").Append(accessor).Append(" != ").Append(v.ValidAnyOf[0]);
      for (var i = 1; i < v.ValidAnyOf.Length; ++i)
        sb.Append(" && ").Append(accessor).Append(" != ").Append(v.ValidAnyOf[i]);
      sb.Append(") throw new global::System.IO.InvalidDataException($\"");
      sb.Append(fieldName).Append(": unexpected value {").Append(accessor).AppendLine("}\");");
    }
  }

  private static void _EmitValidation(StringBuilder sb, FieldModel f) {
    var v = f.Validation;
    var accessor = "_result." + f.Name;

    if (v.ValidString != null || v.ValidBytes != null)
      _EmitMagicValidation(sb, accessor, f.Name, v);
    else
      _EmitScalarValidation(sb, accessor, f.Name, v);
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
    // WireType.DecimalString (legacy AsciiEncoding or explicit [TypeOverride])
    if (f.WireType == 22) {
      if (f.IsEnum)
        sb.Append("(").Append(f.TypeName).Append(")");
      sb.Append("int.Parse(global::System.Text.Encoding.ASCII.GetString(source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append(")).Trim())");
      return;
    }
    if (f.WireType == 21) { // OctalString
      sb.Append("(int)_ParseOctal(source.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("))");
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
    // WireType.DecimalString
    if (f.WireType == 22) {
      var value = f.IsEnum ? "((int)this." + f.Name + ")" : "this." + f.Name;
      sb.Append("global::System.Text.Encoding.ASCII.GetBytes(").Append(value).Append(".ToString(\"D").Append(f.Size).Append("\")).AsSpan(0, ").Append(f.Size).Append(").CopyTo(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("));");
      return;
    }
    if (f.WireType == 21) { // OctalString
      sb.Append("_WriteOctal(destination.Slice(").Append(f.Offset).Append(", ").Append(f.Size).Append("), this.").Append(f.Name).Append(");");
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

  private static void _GenerateFieldMap(StringBuilder sb, HeaderModel model) {
    sb.AppendLine("  /// <summary>Returns the field map describing each field's name, offset, and size for hex editor coloring.</summary>");
    sb.Append("  public static global::FileFormat.Core.HeaderFieldDescriptor[] GetGeneratedFieldMap() => new global::FileFormat.Core.HeaderFieldDescriptor[] {");

    if (model.IsSequential) {
      // Compute offsets from sequential layout (fixed-size fields only; variable-length fields get offset -1)
      var pos = 0;
      for (var i = 0; i < model.Fields.Length; ++i) {
        var f = model.Fields[i];
        if (i > 0) sb.Append(",");
        sb.AppendLine();
        sb.Append("    new(\"").Append(f.Name).Append("\", ").Append(pos).Append(", ").Append(f.Size).Append(")");
        pos += f.IsNullTermString ? 0 : f.Size; // variable-length fields don't advance a known offset
      }
    } else {
      for (var i = 0; i < model.Fields.Length; ++i) {
        var f = model.Fields[i];
        if (i > 0) sb.Append(",");
        sb.AppendLine();
        sb.Append("    new(\"").Append(f.Name).Append("\", ").Append(f.Offset).Append(", ").Append(f.Size).Append(")");
      }
    }

    sb.AppendLine();
    sb.AppendLine("  };");
  }

  // ============================================================================
  // [Repeat] codegen
  // ============================================================================

  private static void _EmitRepeatRead(StringBuilder sb, FieldModel f, string endianSuffix, string indent, string localName, bool hasCondition) {
    var r = f.Repeat;
    var countExpr = r.FixedCount >= 0 ? r.FixedCount.ToString() : "_" + r.CountField;

    // Determine element type from array type
    var elemType = f.TypeName.EndsWith("[]") ? f.TypeName.Substring(0, f.TypeName.Length - 2) : f.TypeName;
    var elemSize = _InferSizeFromTypeName(elemType);

    var decl = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
    sb.Append(decl).Append(" = new ").Append(elemType).Append("[").Append(countExpr).AppendLine("];");
    sb.Append(indent).Append("for (var _ri = 0; _ri < ").Append(countExpr).AppendLine("; ++_ri) {");

    // Check if element is a sub-struct
    var isSubStruct = elemType != "byte" && elemType != "sbyte" && elemType != "short" && elemType != "ushort"
                      && elemType != "int" && elemType != "uint" && elemType != "long" && elemType != "ulong"
                      && elemType != "float" && elemType != "double"
                      && !elemType.StartsWith("global::System.");

    if (isSubStruct && elemSize > 0) {
      sb.Append(indent).Append("  ").Append(localName).Append("[_ri] = ").Append(elemType).Append(".ReadFrom(source.Slice(_pos, ").Append(elemSize).AppendLine("));");
      sb.Append(indent).Append("  _pos += ").Append(elemSize).AppendLine(";");
    } else if (elemSize == 1) {
      sb.Append(indent).Append("  ").Append(localName).AppendLine("[_ri] = source[_pos++];");
    } else if (elemSize > 1) {
      var method = _GetSeqReadTypeName_FromName(elemType, elemSize);
      sb.Append(indent).Append("  ").Append(localName).Append("[_ri] = global::System.Buffers.Binary.BinaryPrimitives.Read").Append(method).Append(endianSuffix).AppendLine("(source[_pos..]);");
      sb.Append(indent).Append("  _pos += ").Append(elemSize).AppendLine(";");
    }

    sb.Append(indent).AppendLine("}");
  }

  private static int _InferSizeFromTypeName(string typeName) {
    switch (typeName) {
      case "byte": case "sbyte": case "global::System.Byte": case "global::System.SByte": return 1;
      case "short": case "ushort": case "global::System.Int16": case "global::System.UInt16": return 2;
      case "int": case "uint": case "float": case "global::System.Int32": case "global::System.UInt32": case "global::System.Single": return 4;
      case "long": case "ulong": case "double": case "global::System.Int64": case "global::System.UInt64": case "global::System.Double": return 8;
      default: return 0;
    }
  }

  private static string _GetSeqReadTypeName_FromName(string typeName, int size) {
    switch (typeName) {
      case "short": case "global::System.Int16": return "Int16";
      case "ushort": case "global::System.UInt16": return "UInt16";
      case "int": case "global::System.Int32": return "Int32";
      case "uint": case "global::System.UInt32": return "UInt32";
      case "long": case "global::System.Int64": return "Int64";
      case "ulong": case "global::System.UInt64": return "UInt64";
      case "float": case "global::System.Single": return "Single";
      case "double": case "global::System.Double": return "Double";
      default: return size switch { 2 => "Int16", 4 => "Int32", 8 => "Int64", _ => "Int32" };
    }
  }

  // ============================================================================
  // WireType conversion codegen
  // ============================================================================

  // WireType enum values (must match FileFormat.Core.WireType)
  private const int _WT_BCD = 1;
  private const int _WT_ZIGZAG = 2;
  private const int _WT_GRAYCODE = 3;
  private const int _WT_VLQ = 4;
  private const int _WT_ULEB128 = 5;
  private const int _WT_SLEB128 = 6;
  private const int _WT_Q15_16 = 7;
  private const int _WT_Q1_15 = 8;
  private const int _WT_Q8_8 = 9;
  private const int _WT_UQ16_16 = 10;
  private const int _WT_FLOAT16 = 11;
  private const int _WT_M4E3 = 12;
  private const int _WT_M3E5 = 13;
  private const int _WT_BFLOAT16 = 14;
  private const int _WT_UINT24 = 15;
  private const int _WT_INT24 = 16;
  private const int _WT_UINT48 = 17;
  private const int _WT_OCTALSTRING = 18 - 1; // shifted: OctalString=17 in the enum? Let me recount
  // Actually let me just match the enum order from TypeOverrideAttribute.cs:
  // BCD=1, ZigZag=2, GrayCode=3, VLQ=4, ULEB128=5, SLEB128=6,
  // Q15_16=7, Q1_15=8, Q8_8=9, UQ16_16=10,
  // Float16=11, M4E3=12, M3E5=13, BFloat16=14,
  // UInt24=15, Int24=16, UInt48=17, Int96=18, UInt128=19, Int128=20,
  // OctalString=21, DecimalString=22, HexString=23

  /// <summary>Returns true if this WireType needs custom read/write codegen.</summary>
  private static bool _HasWireTypeOverride(FieldModel f) => f.WireType > 0;

  /// <summary>Emits a read expression for a field with [TypeOverride]. Returns the C# expression as a string.</summary>
  private static void _EmitWireTypeSeqRead(StringBuilder sb, FieldModel f, string endianSuffix) {
    var localName = "_" + f.Name;
    switch (f.WireType) {
      case 1: // BCD
        sb.Append("    var ").Append(localName).Append(" = _DecodeBCD(source.Slice(_pos, ").Append(f.Size).AppendLine("));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 2: // ZigZag
        sb.Append("    var _raw").Append(f.Name).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32").Append(endianSuffix).AppendLine("(source[_pos..]);");
        sb.Append("    var ").Append(localName).Append(" = (int)((_raw").Append(f.Name).Append(" >> 1) ^ -(int)(_raw").Append(f.Name).AppendLine(" & 1));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 3: // GrayCode
        sb.Append("    var _raw").Append(f.Name).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32").Append(endianSuffix).AppendLine("(source[_pos..]);");
        sb.Append("    var ").Append(localName).Append(" = (int)_DecodeGray(_raw").Append(f.Name).AppendLine(");");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 7: // Q15_16
        sb.Append("    var _raw").Append(f.Name).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32").Append(endianSuffix).AppendLine("(source[_pos..]);");
        sb.Append("    var ").Append(localName).Append(" = _raw").Append(f.Name).AppendLine(" / 65536.0;");
        sb.Append("    _pos += 4;").AppendLine();
        break;
      case 9: // Q8_8
        sb.Append("    var _raw").Append(f.Name).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16").Append(endianSuffix).AppendLine("(source[_pos..]);");
        sb.Append("    var ").Append(localName).Append(" = _raw").Append(f.Name).AppendLine(" / 256.0;");
        sb.Append("    _pos += 2;").AppendLine();
        break;
      case 10: // UQ16_16
        sb.Append("    var _raw").Append(f.Name).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32").Append(endianSuffix).AppendLine("(source[_pos..]);");
        sb.Append("    var ").Append(localName).Append(" = _raw").Append(f.Name).AppendLine(" / 65536.0;");
        sb.Append("    _pos += 4;").AppendLine();
        break;
      case 15: // UInt24
        if (f.Endianness == "Big")
          sb.Append("    var ").Append(localName).Append(" = (source[_pos] << 16) | (source[_pos + 1] << 8) | source[_pos + 2];").AppendLine();
        else
          sb.Append("    var ").Append(localName).Append(" = source[_pos] | (source[_pos + 1] << 8) | (source[_pos + 2] << 16);").AppendLine();
        sb.AppendLine("    _pos += 3;");
        break;
      case 16: // Int24
        if (f.Endianness == "Big")
          sb.Append("    var _raw").Append(f.Name).Append(" = (source[_pos] << 16) | (source[_pos + 1] << 8) | source[_pos + 2];").AppendLine();
        else
          sb.Append("    var _raw").Append(f.Name).Append(" = source[_pos] | (source[_pos + 1] << 8) | (source[_pos + 2] << 16);").AppendLine();
        sb.Append("    var ").Append(localName).Append(" = _raw").Append(f.Name).Append(" > 0x7FFFFF ? _raw").Append(f.Name).AppendLine(" - 0x1000000 : _raw" + f.Name + ";");
        sb.AppendLine("    _pos += 3;");
        break;
      case 21: // OctalString
        sb.Append("    var ").Append(localName).Append(" = _ParseOctal(source.Slice(_pos, ").Append(f.Size).AppendLine("));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 22: // DecimalString
        if (f.IsEnum)
          sb.Append("    var ").Append(localName).Append(" = (").Append(f.TypeName).Append(")");
        else
          sb.Append("    var ").Append(localName).Append(" = ");
        sb.Append("int.Parse(global::System.Text.Encoding.ASCII.GetString(source.Slice(_pos, ").Append(f.Size).AppendLine(")).Trim());");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 23: // HexString
        sb.Append("    var ").Append(localName).Append(" = int.Parse(global::System.Text.Encoding.ASCII.GetString(source.Slice(_pos, ").Append(f.Size).AppendLine(")).Trim(), global::System.Globalization.NumberStyles.HexNumber);");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      default:
        sb.Append("    var ").Append(localName).AppendLine(" = default; /* unsupported WireType */");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
    }
  }

  /// <summary>Emits a write statement for a field with [TypeOverride].</summary>
  private static void _EmitWireTypeSeqWrite(StringBuilder sb, FieldModel f, string endianSuffix) {
    var value = "this." + f.Name;
    switch (f.WireType) {
      case 1: // BCD
        sb.Append("    _EncodeBCD(destination.Slice(_pos, ").Append(f.Size).Append("), ").Append(value).AppendLine(");");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 2: // ZigZag
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32").Append(endianSuffix).Append("(destination[_pos..], (uint)((").Append(value).Append(" << 1) ^ (").Append(value).AppendLine(" >> 31)));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 3: // GrayCode
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32").Append(endianSuffix).Append("(destination[_pos..], _EncodeGray((uint)").Append(value).AppendLine("));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 7: // Q15_16
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.WriteInt32").Append(endianSuffix).Append("(destination[_pos..], (int)(").Append(value).AppendLine(" * 65536.0));");
        sb.AppendLine("    _pos += 4;");
        break;
      case 9: // Q8_8
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16").Append(endianSuffix).Append("(destination[_pos..], (ushort)(").Append(value).AppendLine(" * 256.0));");
        sb.AppendLine("    _pos += 2;");
        break;
      case 10: // UQ16_16
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32").Append(endianSuffix).Append("(destination[_pos..], (uint)(").Append(value).AppendLine(" * 65536.0));");
        sb.AppendLine("    _pos += 4;");
        break;
      case 15: // UInt24
        if (f.Endianness == "Big") {
          sb.Append("    destination[_pos] = (byte)(").Append(value).Append(" >> 16); destination[_pos + 1] = (byte)(").Append(value).Append(" >> 8); destination[_pos + 2] = (byte)").Append(value).AppendLine(";");
        } else {
          sb.Append("    destination[_pos] = (byte)").Append(value).Append("; destination[_pos + 1] = (byte)(").Append(value).Append(" >> 8); destination[_pos + 2] = (byte)(").Append(value).AppendLine(" >> 16);");
        }
        sb.AppendLine("    _pos += 3;");
        break;
      case 16: // Int24 — same write as UInt24
        if (f.Endianness == "Big") {
          sb.Append("    destination[_pos] = (byte)(").Append(value).Append(" >> 16); destination[_pos + 1] = (byte)(").Append(value).Append(" >> 8); destination[_pos + 2] = (byte)").Append(value).AppendLine(";");
        } else {
          sb.Append("    destination[_pos] = (byte)").Append(value).Append("; destination[_pos + 1] = (byte)(").Append(value).Append(" >> 8); destination[_pos + 2] = (byte)(").Append(value).AppendLine(" >> 16);");
        }
        sb.AppendLine("    _pos += 3;");
        break;
      case 21: // OctalString
        sb.Append("    _WriteOctal(destination.Slice(_pos, ").Append(f.Size).Append("), ").Append(value).AppendLine(");");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 22: // DecimalString
        var val22 = f.IsEnum ? "((int)" + value + ")" : value;
        sb.Append("    global::System.Text.Encoding.ASCII.GetBytes(").Append(val22).Append(".ToString(\"D").Append(f.Size).Append("\")).AsSpan(0, ").Append(f.Size).Append(").CopyTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      case 23: // HexString
        sb.Append("    global::System.Text.Encoding.ASCII.GetBytes(").Append(value).Append(".ToString(\"X").Append(f.Size).Append("\")).AsSpan(0, ").Append(f.Size).Append(").CopyTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
      default:
        sb.Append("    /* unsupported WireType write for ").Append(f.Name).AppendLine(" */");
        sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
        break;
    }
  }

  /// <summary>Emits static helper methods used by WireType conversions, if any fields need them.</summary>
  private static void _EmitWireTypeHelpers(StringBuilder sb, HeaderModel model) {
    var needsBCD = false;
    var needsGray = false;
    var needsOctal = false;
    foreach (var f in model.Fields) {
      switch (f.WireType) {
        case 1: needsBCD = true; break;
        case 3: needsGray = true; break;
        case 21: needsOctal = true; break;
      }
    }

    if (needsBCD) {
      sb.AppendLine();
      sb.AppendLine("  private static long _DecodeBCD(global::System.ReadOnlySpan<byte> data) {");
      sb.AppendLine("    long result = 0;");
      sb.AppendLine("    for (var i = 0; i < data.Length; ++i) { result = result * 100 + (data[i] >> 4) * 10 + (data[i] & 0x0F); }");
      sb.AppendLine("    return result;");
      sb.AppendLine("  }");
      sb.AppendLine("  private static void _EncodeBCD(global::System.Span<byte> dest, long value) {");
      sb.AppendLine("    for (var i = dest.Length - 1; i >= 0; --i) { dest[i] = (byte)(((value / 10 % 10) << 4) | (value % 10)); value /= 100; }");
      sb.AppendLine("  }");
    }

    if (needsGray) {
      sb.AppendLine();
      sb.AppendLine("  private static uint _DecodeGray(uint g) { g ^= g >> 16; g ^= g >> 8; g ^= g >> 4; g ^= g >> 2; g ^= g >> 1; return g; }");
      sb.AppendLine("  private static uint _EncodeGray(uint n) => n ^ (n >> 1);");
    }

    if (needsOctal) {
      sb.AppendLine();
      sb.AppendLine("  private static long _ParseOctal(global::System.ReadOnlySpan<byte> field) {");
      sb.AppendLine("    if (field.Length > 0 && field[0] >= 0x80) { long r = 0; for (var i = 0; i < field.Length; ++i) r = (r << 8) | field[i]; return r & 0x7FFFFFFFFFFFFFFF; }");
      sb.AppendLine("    var s = global::System.Text.Encoding.ASCII.GetString(field).TrimEnd('\\0', ' ');");
      sb.AppendLine("    return s.Length == 0 ? 0 : global::System.Convert.ToInt64(s, 8);");
      sb.AppendLine("  }");
      sb.AppendLine("  private static void _WriteOctal(global::System.Span<byte> dest, long value) {");
      sb.AppendLine("    var s = global::System.Convert.ToString(value, 8).PadLeft(dest.Length - 1, '0');");
      sb.AppendLine("    global::System.Text.Encoding.ASCII.GetBytes(s).AsSpan(0, dest.Length - 1).CopyTo(dest);");
      sb.AppendLine("    dest[dest.Length - 1] = 0;");
      sb.AppendLine("  }");
    }
  }

  // ============================================================================
  // Sequential mode codegen
  // ============================================================================

  private static (string encoding, int codePage) _ParseStringEncodingFull(AttributeData attr) {
    var encoding = "ASCII";
    var codePage = 0;

    if (attr.ConstructorArguments.Length >= 1) {
      var val = attr.ConstructorArguments[0].Value;
      if (val is int intVal)
        encoding = intVal switch {
          1 => "UTF8", 2 => "Latin1", 3 => "Unicode", 4 => "UnicodeBE",
          5 => "EBCDIC", 6 => "PETSCII", 7 => "ATASCII", 8 => "Custom",
          _ => "ASCII"
        };
      else if (val is string strVal)
        encoding = strVal;
    }

    foreach (var named in attr.NamedArguments)
      if (named.Key == "CodePage" && named.Value.Value is int cp)
        codePage = cp;

    return (encoding, codePage);
  }

  private static string _ParseStringEncoding(AttributeData attr) => _ParseStringEncodingFull(attr).encoding;

  private static string _GetEncodingExpression(string encoding) {
    switch (encoding?.ToUpperInvariant()) {
      case "ASCII": return "global::System.Text.Encoding.ASCII";
      case "UTF-8":
      case "UTF8": return "global::System.Text.Encoding.UTF8";
      case "LATIN1":
      case "ISO-8859-1": return "global::System.Text.Encoding.Latin1";
      case "UNICODE": return "global::System.Text.Encoding.Unicode";
      case "UNICODEBE": return "global::System.Text.Encoding.BigEndianUnicode";
      case "EBCDIC": return "global::System.Text.Encoding.GetEncoding(37)"; // EBCDIC US-Canada
      case "PETSCII": return "global::System.Text.Encoding.GetEncoding(\"PETSCII\")"; // requires custom provider
      case "ATASCII": return "global::System.Text.Encoding.GetEncoding(\"ATASCII\")"; // requires custom provider
      default: return "global::System.Text.Encoding.ASCII";
    }
  }

  private static void _GenerateSeqReadFrom(StringBuilder sb, HeaderModel model) {
    var useInit = model.UseInitSyntax;

    sb.Append("  public static ").Append(model.Name).AppendLine(" ReadFrom(global::System.ReadOnlySpan<byte> source) {");
    sb.AppendLine("    var _pos = 0;");

    // Read each field into a local variable
    for (var i = 0; i < model.Fields.Length; ++i) {
      var f = model.Fields[i];
      _EmitSeqFieldRead(sb, f, model);
    }

    // Emit validation
    var hasValidation = _HasValidation(model);
    if (hasValidation) {
      sb.AppendLine();
      foreach (var f in model.Fields) {
        if (f.Validation == null) continue;
        _EmitSeqValidation(sb, f);
      }
    }

    // Build result
    sb.AppendLine();
    var opener = useInit ? "new() {" : "new(";
    var closer = useInit ? "};": ");";
    sb.Append("    return ").AppendLine(opener);
    for (var i = 0; i < model.Fields.Length; ++i) {
      var f = model.Fields[i];
      sb.Append("      ");
      if (useInit)
        sb.Append(f.Name).Append(" = ");
      sb.Append("_").Append(f.Name);
      sb.AppendLine(i < model.Fields.Length - 1 ? "," : "");
    }
    sb.Append("    ").AppendLine(closer);
    sb.AppendLine("  }");
  }

  private static void _EmitSeqFieldRead(StringBuilder sb, FieldModel f, HeaderModel model) {
    var localName = "_" + f.Name;
    var endianSuffix = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";

    // [FieldOffset(N)] — jump cursor to absolute position
    if (f.FieldOffset >= 0)
      sb.Append("    _pos = ").Append(f.FieldOffset).AppendLine(";");

    // [If] — conditional field
    var hasCondition = f.Condition != null;
    if (hasCondition) {
      var c = f.Condition;
      var condField = "_" + c.FieldName;
      var opExpr = c.Op switch {
        0 => $"{condField} == {c.Value}",      // Equal
        1 => $"{condField} != {c.Value}",      // NotEqual
        2 => $"{condField} > {c.Value}",       // Greater
        3 => $"{condField} >= {c.Value}",      // GreaterOrEqual
        4 => $"{condField} < {c.Value}",       // Less
        5 => $"{condField} <= {c.Value}",      // LessOrEqual
        6 => $"(((int){condField}) & {c.Value}) != 0",  // HasFlag
        _ => "true"
      };
      sb.Append("    ").Append(f.TypeName).Append("? ").Append(localName).AppendLine(" = null;");
      sb.Append("    if (").Append(opExpr).AppendLine(") {");
    }

    var indent = hasCondition ? "      " : "    ";

    // [Repeat] — array field
    if (f.Repeat != null) {
      _EmitRepeatRead(sb, f, endianSuffix, indent, localName, hasCondition);
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // [SizedBy] — dynamic size from another field
    if (f.SizedByField != null) {
      var sizeExpr = "_" + f.SizedByField;
      if (f.TypeName is "byte[]" or "global::System.Byte[]") {
        if (hasCondition)
          sb.Append(indent).Append(localName).Append(" = source.Slice(_pos, ").Append(sizeExpr).AppendLine(").ToArray();");
        else
          sb.Append(indent).Append("var ").Append(localName).Append(" = source.Slice(_pos, ").Append(sizeExpr).AppendLine(").ToArray();");
        sb.Append(indent).Append("_pos += ").Append(sizeExpr).AppendLine(";");
      } else if (f.TypeName is "string" or "global::System.String") {
        var enc = _GetEncodingExpression(f.StringEncoding ?? "ASCII");
        if (hasCondition)
          sb.Append(indent).Append(localName).Append(" = ").Append(enc).Append(".GetString(source.Slice(_pos, ").Append(sizeExpr).AppendLine(")).TrimEnd('\\0');");
        else
          sb.Append(indent).Append("var ").Append(localName).Append(" = ").Append(enc).Append(".GetString(source.Slice(_pos, ").Append(sizeExpr).AppendLine(")).TrimEnd('\\0');");
        sb.Append(indent).Append("_pos += ").Append(sizeExpr).AppendLine(";");
      }
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // [TypeOverride] — custom wire format
    if (_HasWireTypeOverride(f)) {
      _EmitWireTypeSeqRead(sb, f, endianSuffix);
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // Sub-struct: delegate to its own ReadFrom
    if (f.IsSubStruct) {
      var decl = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
      sb.Append(decl).Append(" = ").Append(f.TypeName).Append(".ReadFrom(source.Slice(_pos, ").Append(f.Size).AppendLine("));");
      sb.Append(indent).Append("_pos += ").Append(f.Size).AppendLine(";");
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // NullTermString: variable-length, reads until 0x00
    if (f.IsNullTermString) {
      var enc = _GetEncodingExpression(f.StringEncoding);
      sb.Append(indent).Append("var _end").Append(f.Name).AppendLine(" = source[_pos..].IndexOf((byte)0);");
      sb.Append(indent).Append("if (_end").Append(f.Name).AppendLine(" < 0) throw new global::System.IO.InvalidDataException(\"Unterminated string for field " + f.Name + "\");");
      var decl0 = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
      sb.Append(decl0).Append(" = ").Append(enc).Append(".GetString(source.Slice(_pos, _end").Append(f.Name).AppendLine("));");
      sb.Append(indent).Append("_pos += _end").Append(f.Name).AppendLine(" + 1; // skip null terminator");
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // StringField: fixed-size encoded string
    if (f.StringEncoding != null && f.TypeName is "string" or "global::System.String") {
      var enc = _GetEncodingExpression(f.StringEncoding);
      var decl1 = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
      sb.Append(decl1).Append(" = ").Append(enc).Append(".GetString(source.Slice(_pos, ").Append(f.Size).AppendLine(")).TrimEnd('\\0');");
      sb.Append(indent).Append("_pos += ").Append(f.Size).AppendLine(";");
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // byte[] field
    if (f.TypeName is "byte[]" or "global::System.Byte[]") {
      var decl2 = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
      sb.Append(decl2).Append(" = source.Slice(_pos, ").Append(f.Size).AppendLine(").ToArray();");
      sb.Append(indent).Append("_pos += ").Append(f.Size).AppendLine(";");
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // string (no StringField attribute — treat as ASCII, for backward compat)
    if (f.TypeName is "string" or "global::System.String") {
      var decl3 = hasCondition ? $"{indent}{localName}" : $"{indent}var {localName}";
      sb.Append(decl3).Append(" = global::System.Text.Encoding.ASCII.GetString(source.Slice(_pos, ").Append(f.Size).AppendLine(")).TrimEnd('\\0');");
      sb.Append(indent).Append("_pos += ").Append(f.Size).AppendLine(";");
      if (hasCondition) sb.AppendLine("    }");
      return;
    }

    // Enum types
    var isEnum = f.IsEnum;
    if (hasCondition)
      sb.Append(indent).Append(localName).Append(" = ");
    else
      sb.Append(indent).Append("var ").Append(localName).Append(" = ");

    if (isEnum)
      sb.Append("(").Append(f.TypeName).Append(")(");

    switch (f.Size) {
      case 1:
        sb.Append("source[_pos]");
        break;
      case 2:
        sb.Append("global::System.Buffers.Binary.BinaryPrimitives.Read");
        sb.Append(_GetSeqReadTypeName(f, 2)).Append(endianSuffix).Append("(source[_pos..])");
        break;
      case 4:
        sb.Append("global::System.Buffers.Binary.BinaryPrimitives.Read");
        sb.Append(_GetSeqReadTypeName(f, 4)).Append(endianSuffix).Append("(source[_pos..])");
        break;
      case 8:
        sb.Append("global::System.Buffers.Binary.BinaryPrimitives.Read");
        sb.Append(_GetSeqReadTypeName(f, 8)).Append(endianSuffix).Append("(source[_pos..])");
        break;
      default:
        sb.Append("default /* unsupported seq field size ").Append(f.Size).Append(" */");
        break;
    }

    if (isEnum) sb.Append(")");
    sb.AppendLine(";");
    sb.Append(indent).Append("_pos += ").Append(f.Size).AppendLine(";");

    if (hasCondition) sb.AppendLine("    }");
  }

  private static string _GetSeqReadTypeName(FieldModel f, int size) {
    var baseType = f.IsEnum ? (f.UnderlyingEnumType ?? "global::System.Int32") : f.TypeName;
    switch (baseType) {
      case "short": case "global::System.Int16": return "Int16";
      case "ushort": case "global::System.UInt16": return "UInt16";
      case "int": case "global::System.Int32": return "Int32";
      case "uint": case "global::System.UInt32": return "UInt32";
      case "long": case "global::System.Int64": return "Int64";
      case "ulong": case "global::System.UInt64": return "UInt64";
      case "float": case "global::System.Single": return size == 2 ? "Half" : "Single";
      case "double": case "global::System.Double": return "Double";
      default:
        switch (size) { case 2: return "Int16"; case 4: return "Int32"; case 8: return "Int64"; default: return "Int32"; }
    }
  }

  private static void _EmitSeqValidation(StringBuilder sb, FieldModel f) {
    var v = f.Validation;
    var accessor = "_" + f.Name;

    if (v.ValidString != null || v.ValidBytes != null)
      _EmitMagicValidation(sb, accessor, f.Name, v);
    else
      _EmitScalarValidation(sb, accessor, f.Name, v);
  }

  private static void _GenerateSeqWriteTo(StringBuilder sb, HeaderModel model) {
    sb.AppendLine("  public void WriteTo(global::System.Span<byte> destination) {");
    sb.AppendLine("    var _pos = 0;");

    foreach (var f in model.Fields) {
      _EmitSeqFieldWrite(sb, f, model);
    }

    sb.AppendLine("  }");
  }

  private static void _EmitSeqFieldWrite(StringBuilder sb, FieldModel f, HeaderModel model) {
    var endianSuffix = f.Endianness == "Big" ? "BigEndian" : "LittleEndian";

    // [FieldOffset(N)] — jump cursor to absolute position
    if (f.FieldOffset >= 0)
      sb.Append("    _pos = ").Append(f.FieldOffset).AppendLine(";");

    // [TypeOverride] — custom wire format
    if (_HasWireTypeOverride(f)) {
      _EmitWireTypeSeqWrite(sb, f, endianSuffix);
      return;
    }

    // Sub-struct: delegate to its own WriteTo
    if (f.IsSubStruct) {
      sb.Append("    this.").Append(f.Name).Append(".WriteTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
      sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
      return;
    }

    // NullTermString: write string + null terminator
    if (f.IsNullTermString) {
      var enc = _GetEncodingExpression(f.StringEncoding);
      sb.Append("    if (this.").Append(f.Name).AppendLine(" != null) {");
      sb.Append("      var _bytes").Append(f.Name).Append(" = ").Append(enc).Append(".GetBytes(this.").Append(f.Name).AppendLine(");");
      sb.Append("      _bytes").Append(f.Name).Append(".AsSpan().CopyTo(destination[_pos..]); _pos += _bytes").Append(f.Name).AppendLine(".Length;");
      sb.AppendLine("    }");
      sb.AppendLine("    destination[_pos++] = 0; // null terminator");
      return;
    }

    // StringField: fixed-size encoded string
    if (f.StringEncoding != null && f.TypeName is "string" or "global::System.String") {
      var enc = _GetEncodingExpression(f.StringEncoding);
      sb.Append("    destination.Slice(_pos, ").Append(f.Size).AppendLine(").Clear();");
      sb.Append("    if (this.").Append(f.Name).Append(" != null) ").Append(enc).Append(".GetBytes(this.").Append(f.Name).Append(" ?? \"\").AsSpan().CopyTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
      sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
      return;
    }

    // [Repeat] array write
    if (f.Repeat != null) {
      var r = f.Repeat;
      var countExpr = r.FixedCount >= 0 ? r.FixedCount.ToString() : "this." + r.CountField;
      var elemType = f.TypeName.EndsWith("[]") ? f.TypeName.Substring(0, f.TypeName.Length - 2) : f.TypeName;
      var elemSize = _InferSizeFromTypeName(elemType);
      sb.Append("    if (this.").Append(f.Name).AppendLine(" != null)");
      sb.Append("      for (var _wi = 0; _wi < ").Append(countExpr).AppendLine("; ++_wi) {");
      if (elemSize == 1) {
        sb.Append("        destination[_pos++] = (byte)this.").Append(f.Name).AppendLine("[_wi];");
      } else if (elemSize > 1) {
        var method = _GetBinaryPrimitivesMethodName(elemType, elemSize, "Write", endianSuffix);
        sb.Append("        global::System.Buffers.Binary.BinaryPrimitives.").Append(method).Append("(destination[_pos..], this.").Append(f.Name).AppendLine("[_wi]);");
        sb.Append("        _pos += ").Append(elemSize).AppendLine(";");
      }
      sb.AppendLine("      }");
      if (elemSize != 1)
        sb.Append("    else _pos += ").Append(f.Size).AppendLine(";");
      return;
    }

    // byte[] field
    if (f.TypeName is "byte[]" or "global::System.Byte[]") {
      sb.Append("    if (this.").Append(f.Name).Append(" != null) this.").Append(f.Name).Append(".AsSpan().CopyTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
      sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
      return;
    }

    // string (no StringField — ASCII default)
    if (f.TypeName is "string" or "global::System.String") {
      sb.Append("    destination.Slice(_pos, ").Append(f.Size).AppendLine(").Clear();");
      sb.Append("    if (this.").Append(f.Name).Append(" != null) global::System.Text.Encoding.ASCII.GetBytes(this.").Append(f.Name).Append(").AsSpan().CopyTo(destination.Slice(_pos, ").Append(f.Size).AppendLine("));");
      sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
      return;
    }

    // Enum cast for write
    var value = f.IsEnum ? "(" + _GetWriteCastType(f, f.Size) + ")this." + f.Name : "this." + f.Name;

    switch (f.Size) {
      case 1:
        if (f.IsEnum)
          sb.Append("    destination[_pos] = (byte)this.").Append(f.Name).AppendLine(";");
        else
          sb.Append("    destination[_pos] = this.").Append(f.Name).AppendLine(";");
        break;
      case 2: case 4: case 8: {
        var method = _GetBinaryPrimitivesMethodName(f.IsEnum ? (f.UnderlyingEnumType ?? "global::System.Int32") : f.TypeName, f.Size, "Write", endianSuffix);
        sb.Append("    global::System.Buffers.Binary.BinaryPrimitives.").Append(method).Append("(destination[_pos..], ").Append(value).AppendLine(");");
        break;
      }
      default:
        sb.Append("    /* unsupported seq field size ").Append(f.Size).Append(" for ").Append(f.Name).AppendLine(" */");
        break;
    }
    sb.Append("    _pos += ").Append(f.Size).AppendLine(";");
  }
}