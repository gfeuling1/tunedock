using System;
using System.Windows.Input;

namespace TuneDock.ViewModels;

/// <summary>Minimal synchronous ICommand.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        try { _execute(); }
        catch (Exception) { }
    }
}
