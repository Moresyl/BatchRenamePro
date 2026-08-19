using System.Collections.ObjectModel;
using System.ComponentModel;
using BatchRenamePro.App.Localization;
using BatchRenamePro.Core.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BatchRenamePro.App.ViewModels;

/// <summary>A rule problem, translated and ready to show under the card that caused it.</summary>
/// <param name="Message">The translated text.</param>
/// <param name="IsError">Whether the problem blocks the run, as opposed to merely warning about it.</param>
public sealed record RuleProblem(string Message, bool IsError);

/// <summary>
/// The card around one rule: its heading, its problems, and the buttons that reorder or delete it.
/// </summary>
/// <remarks>
/// The rule itself is the editor's data context — core rules already implement
/// <see cref="INotifyPropertyChanged"/> with settable properties, so the per-type editors two-way
/// bind straight to them and there is no second copy of every setting to keep in sync. This class
/// only owns what the card adds around the rule.
/// </remarks>
public sealed partial class RuleCardViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _localizer;
    private readonly ObservableCollection<RuleProblem> _problems = [];
    private bool _disposed;

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Creates a card for a rule.</summary>
    /// <param name="rule">The rule being edited.</param>
    /// <param name="localizer">Translates the heading and the problems.</param>
    public RuleCardViewModel(IRenameRule rule, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Rule = rule;
        _localizer = localizer;
        Descriptor = RuleCatalog.Describe(rule);
        Problems = new ReadOnlyObservableCollection<RuleProblem>(_problems);

        Rule.PropertyChanged += OnRuleChanged;
        _localizer.PropertyChanged += OnLanguageChanged;
        Revalidate();
    }

    /// <summary>Raised whenever anything on the rule changes, so the preview can rebuild.</summary>
    public event EventHandler? Changed;

    /// <summary>The rule this card wraps.</summary>
    public IRenameRule Rule { get; }

    /// <summary>Menu metadata for the rule's type, or <see langword="null"/> for an unknown type.</summary>
    public RuleDescriptor? Descriptor { get; }

    /// <summary>The card's heading.</summary>
    public string Title => Descriptor is null ? Rule.TypeKey : _localizer[Descriptor.NameCode];

    /// <summary>The one-line description under the heading.</summary>
    public string Summary => Descriptor is null ? string.Empty : _localizer[Descriptor.SummaryCode];

    /// <summary>Resource key of the icon drawn on the card.</summary>
    public string IconKey => Descriptor?.IconKey ?? "Icon.Pattern";

    /// <summary>Everything wrong with the rule's current settings.</summary>
    public ReadOnlyObservableCollection<RuleProblem> Problems { get; }

    /// <summary>Whether the card has anything to complain about.</summary>
    public bool HasProblems => _problems.Count > 0;

    /// <summary>Whether one of those problems blocks the run.</summary>
    public bool HasError => _problems.Any(problem => problem.IsError);

    /// <summary>Whether the rule takes part in the pipeline. Bound to the card's switch.</summary>
    public bool IsEnabled
    {
        get => Rule.IsEnabled;
        set
        {
            if (Rule.IsEnabled == value) return;
            Rule.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A card outlives its rule only if someone forgets to unhook these; the rules list disposes
        // cards as it removes them so a long editing session does not accumulate dead subscriptions.
        Rule.PropertyChanged -= OnRuleChanged;
        _localizer.PropertyChanged -= OnLanguageChanged;
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IRenameRule.IsEnabled)) OnPropertyChanged(nameof(IsEnabled));

        Revalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        Revalidate();
    }

    private void Revalidate()
    {
        _problems.Clear();

        foreach (var diagnostic in Rule.Validate())
        {
            // The core's own English text is the fallback, so a code added to the engine before it
            // reaches the string tables still says something useful instead of showing its key.
            var message = _localizer[diagnostic.Code];
            if (message == diagnostic.Code) message = diagnostic.Message;

            _problems.Add(new RuleProblem(message, diagnostic.Severity is DiagnosticSeverity.Error));
        }

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasError));
    }
}
