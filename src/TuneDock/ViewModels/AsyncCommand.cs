using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TuneDock.ViewModels;

/// <summary>Minimal ICommand that runs an async action. Enabled state is bound separately.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;

    public AsyncCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try { await _execute(); }
        catch (Exception) { /* media controls are best-effort */ }
    }
}
