namespace UrHmi.Presentation.Shell;

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    void Invoke(Action action);

    T Invoke<T>(Func<T> func);

    Task InvokeAsync(Action action);
}
