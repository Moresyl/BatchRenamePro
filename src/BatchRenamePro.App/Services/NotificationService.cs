using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BatchRenamePro.App.Services;

/// <summary>How a toast reads: its accent colour and icon.</summary>
public enum NotificationKind
{
    /// <summary>Neutral information.</summary>
    Information,

    /// <summary>Something worked.</summary>
    Success,

    /// <summary>Something to be careful about.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error
}

/// <summary>One toast in the corner of the window.</summary>
public sealed partial class Notification : ObservableObject
{
    /// <summary>Creates a toast.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="kind">How it reads.</param>
    public Notification(string message, NotificationKind kind)
    {
        Message = message;
        Kind = kind;
    }

    /// <summary>What the toast says.</summary>
    public string Message { get; }

    /// <summary>How the toast reads.</summary>
    public NotificationKind Kind { get; }

    /// <summary>The label on the optional action button; empty hides it.</summary>
    public string ActionLabel { get; init; } = string.Empty;

    /// <summary>Whether the toast offers an action.</summary>
    public bool HasAction => ActionLabel.Length > 0;

    /// <summary>What the action button does.</summary>
    public Func<Task>? Action { get; init; }

    /// <summary>Raised when the toast should leave the screen.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Takes the toast off screen.</summary>
    [RelayCommand]
    public void Dismiss() => Dismissed?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs the action, then dismisses.</summary>
    [RelayCommand]
    private async Task InvokeAsync()
    {
        if (Action is not null) await Action().ConfigureAwait(true);
        Dismiss();
    }
}

/// <summary>Shows brief messages that do not need an answer.</summary>
public interface INotificationService
{
    /// <summary>The toasts currently on screen, oldest first.</summary>
    ReadOnlyObservableCollection<Notification> Items { get; }

    /// <summary>Shows a toast.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="kind">How it reads.</param>
    /// <param name="actionLabel">Label for an optional button.</param>
    /// <param name="action">What the button does.</param>
    void Show(string message, NotificationKind kind = NotificationKind.Information, string actionLabel = "", Func<Task>? action = null);
}

/// <inheritdoc cref="INotificationService" />
/// <remarks>
/// Toasts replace the message boxes an application like this usually throws at you. "Renamed 42
/// files" does not deserve a modal dialog and an OK button; it deserves a line in the corner that
/// goes away on its own.
/// </remarks>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LifetimeWithAction = TimeSpan.FromSeconds(12);

    private readonly ObservableCollection<Notification> _items = [];

    /// <summary>Creates the service.</summary>
    public NotificationService() => Items = new ReadOnlyObservableCollection<Notification>(_items);

    /// <inheritdoc />
    public ReadOnlyObservableCollection<Notification> Items { get; }

    /// <inheritdoc />
    public void Show(string message, NotificationKind kind = NotificationKind.Information, string actionLabel = "", Func<Task>? action = null)
    {
        var notification = new Notification(message, kind) { ActionLabel = actionLabel, Action = action };

        // A toast the user can act on has to outlive one they only have to read.
        var timer = new DispatcherTimer { Interval = notification.HasAction ? LifetimeWithAction : Lifetime };

        void Remove(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= Remove;
            notification.Dismissed -= Remove;
            _items.Remove(notification);
        }

        timer.Tick += Remove;
        notification.Dismissed += Remove;
        timer.Start();

        // Three is where the stack starts covering the content it is reporting on.
        while (_items.Count >= 3) _items[0].Dismiss();

        _items.Add(notification);
    }
}
