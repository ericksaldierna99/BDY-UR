namespace UrHmi.Tools.Screwdriver.Tests;

internal sealed class FakeSpinHttpApi : ISpinHttpApi
{
    public List<string> Calls { get; } = [];

    public SpinHttpRead Next { get; set; } = new(null, null, null, "HTTP not configured");

    public Func<string, CancellationToken, Task<SpinHttpRead>>? Handler { get; set; }

    public Task<SpinHttpRead> ReadLatestAsync(string baseUrl, CancellationToken cancellationToken)
    {
        Calls.Add(baseUrl);
        if (Handler is not null)
        {
            return Handler(baseUrl, cancellationToken);
        }

        return Task.FromResult(Next);
    }
}
