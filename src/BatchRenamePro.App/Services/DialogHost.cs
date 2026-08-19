using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BatchRenamePro.App.Services;

/// <summary>What a dialog is telling the user, which decides its accent colour and icon.</summary>
public enum DialogKind
{
    /// <summary>A neutral question.</summary>
    Question,

    /// <summary>Something that worked.</summary>
    Success,

    /// <summary>Something to be careful about.</summary>
    Warning,

    /// <summary>Something that failed.</summary>
    Error
}

/// <summary>One modal dialog: its text, its buttons, and the answer it is waiting for.</summary>
public sealed partial class DialogRequest : ObservableObject
{
    private readonly TaskCompletionSource<string?> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    private string _input = string.Empty;

    /// <summary>Creates a dialog.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The body text.</param>
    /// <param name="kind">What the dialog is about.</param>
    public DialogRequest(string title, string message, DialogKind kind = DialogKind.Question)
    {
        Title = title;
        Message = message;
        Kind = kind;
    }

    /// <summary>The heading.</summary>
    public string Title { get; }

    /// <summary>The body text.</summary>
    public string Message { get; }

    /// <summary>What the dialog is about.</summary>
    public DialogKind Kind { get; }

    /// <summary>Whether the dialog has a text box.</summary>
    public bool IsInput { get; init; }

    /// <summary>Whether the confirm button is styled as a destructive action.</summary>
    public bool IsDestructive { get; init; }

    /// <summary>The confirm button's label.</summary>
    public string ConfirmLabel { get; init; } = "OK";

    /// <summary>The dismiss button's label; empty hides the button entirely.</summary>
    public string CancelLabel { get; init; } = string.Empty;

    /// <summary>Whether a dismiss button is shown.</summary>
    public bool CanCancel => CancelLabel.Length > 0;

    /// <summary>Completes once the user answers: the entered text, or <see langword="null"/> if dismissed.</summary>
    public Task<string?> Completion => _answer.Task;

    /// <summary>Accepts the dialog.</summary>
    [RelayCommand]
    public void Confirm() => _answer.TrySetResult(IsInput ? Input : string.Empty);

    /// <summary>Dismisses the dialog.</summary>
    [RelayCommand]
    public void Cancel() => _answer.TrySetResult(null);
}

/// <summary>
/// Holds the dialog the shell is currently showing.
/// </summary>
/// <remarks>
/// This exists so that <see cref="DialogService"/> and the shell view model do not have to know
/// about each other: the service pushes a request in here, the shell binds to <see cref="Current"/>
/// and renders it over the page. Modal behaviour comes from the overlay covering the window, not
/// from a nested message loop, so nothing re-enters while a dialog is open.
/// </remarks>
public sealed partial class DialogHost : ObservableObject
{
    private readonly Queue<DialogRequest> _waiting = new();

    [ObservableProperty]
    private DialogRequest? _current;

    /// <summary>Shows a dialog and waits for the answer.</summary>
    /// <param name="request">The dialog to show.</param>
    public async Task<string?> ShowAsync(DialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Two dialogs at once would stack on top of each other and the one underneath would look
        // like a rendering glitch, so later requests queue instead.
        if (Current is not null) _waiting.Enqueue(request);
        else Current = request;

        try
        {
            return await request.Completion.ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(Current, request))
                Current = _waiting.Count > 0 ? _waiting.Dequeue() : null;
        }
    }
}
