namespace FileFormat.WindowsPe;

/// <summary>Describes one leaf in a PE resource tree.</summary>
/// <remarks>
/// Windows normally addresses resources by type, name/ID and language. Numeric resources expose
/// their IDs; named resources expose their UTF-16 names instead. The descriptor is intentionally
/// read-only: use <see cref="PeResourceFile.ReplaceResource(int,int,System.ReadOnlySpan{byte})"/>
/// or its language-specific overload to replace an existing numeric resource.
/// </remarks>
public sealed class PeResourceInfo {

  /// <summary>Numeric resource type, or <see langword="null"/> when the type is named.</summary>
  public int? TypeId { get; init; }

  /// <summary>Named resource type, or <see langword="null"/> when the type is numeric.</summary>
  public string? TypeName { get; init; }

  /// <summary>Numeric resource name/ID, or <see langword="null"/> when the resource is named.</summary>
  public int? ResourceId { get; init; }

  /// <summary>Named resource name, or <see langword="null"/> when the resource uses a numeric ID.</summary>
  public string? ResourceName { get; init; }

  /// <summary>Numeric language identifier, or <see langword="null"/> when the language key is named.</summary>
  public int? LanguageId { get; init; }

  /// <summary>Named language key, or <see langword="null"/> for the usual numeric LANGID.</summary>
  public string? LanguageName { get; init; }

  /// <summary>Size of the raw resource payload in bytes.</summary>
  public int Size { get; init; }
}
