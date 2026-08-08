using System.Windows.Input;

namespace UDM18.Client.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is T value && (canExecute?.Invoke(value) ?? true);
    public void Execute(object? parameter) { if (parameter is T value) execute(value); }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private CancellationTokenSource? _cts;
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public event Action<Exception>? Failed;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        _cts = new CancellationTokenSource();
        NotifyCanExecuteChanged();
        try { await execute(_cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _running = false;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => _cts?.Cancel();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
