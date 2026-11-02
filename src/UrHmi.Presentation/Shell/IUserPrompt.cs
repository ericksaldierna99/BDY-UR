namespace UrHmi.Presentation.Shell;

public interface IUserPrompt
{
    bool Confirm(string title, string message);

    void Inform(string title, string message);
}
