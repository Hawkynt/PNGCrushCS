namespace FileFormat.Fits;

/// <summary>A single FITS header keyword record.</summary>
public sealed class FitsKeyword {
  public string Name { get; }
  public string? Value { get; }
  public string? Comment { get; }

  public FitsKeyword(string name, string? value, string? comment) {
    this.Name = name;
    this.Value = value;
    this.Comment = comment;
  }
}
