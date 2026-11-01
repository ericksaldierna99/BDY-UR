namespace UrHmi.Presentation.Shell;

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}
