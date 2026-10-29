using UrHmi.Domain.Export;

namespace UrHmi.Domain.Ports;

public interface IExportWriter
{
    string FormatId { get; }

    Task ExportAsync(ExportRequest request, Stream destination, CancellationToken cancellationToken = default);
}
