namespace UrHmi.Domain.Export;

public sealed record ExportRequest(
    string Kind,
    IReadOnlyDictionary<string, string> Parameters);
