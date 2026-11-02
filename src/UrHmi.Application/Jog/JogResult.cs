namespace UrHmi.Application.Jog;

public sealed record JogResult(bool Succeeded, string Message)
{
    public static JogResult Ok(string message) => new(true, message);

    public static JogResult Fail(string message) => new(false, message);
}
