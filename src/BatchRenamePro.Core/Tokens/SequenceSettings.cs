using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BatchRenamePro.Core.Tokens;

/// <summary>
/// The bindable, serializable form of <see cref="SequenceOptions"/>, shared by every rule that
/// generates numbering.
/// </summary>
/// <remarks>
/// Numbering lives on the rule rather than on the batch so two numbering rules in the same pipeline
/// can count differently — for example a chapter number that advances every second file alongside a
/// plain per-file counter.
/// </remarks>
public sealed class SequenceSettings : INotifyPropertyChanged
{
    private int _start = 1;
    private int _step = 1;
    private int _padding = 2;
    private SequenceStyle _style = SequenceStyle.Numeric;
    private int _groupSize = 1;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Value assigned to the first item.</summary>
    public int Start
    {
        get => _start;
        set => Set(ref _start, value);
    }

    /// <summary>Amount added per item, or per group when <see cref="GroupSize"/> is greater than one.</summary>
    public int Step
    {
        get => _step;
        set => Set(ref _step, value);
    }

    /// <summary>Minimum digit width for decimal and base-36 numbering.</summary>
    public int Padding
    {
        get => _padding;
        set => Set(ref _padding, value);
    }

    /// <summary>The alphabet the sequence counts in.</summary>
    public SequenceStyle Style
    {
        get => _style;
        set => Set(ref _style, value);
    }

    /// <summary>How many consecutive items share the same number.</summary>
    public int GroupSize
    {
        get => _groupSize;
        set => Set(ref _groupSize, value);
    }

    /// <summary>Projects the settings onto the immutable options the formatter consumes.</summary>
    public SequenceOptions ToOptions() => new(_start, _step, _padding, _style, Math.Max(1, _groupSize));

    /// <summary>Creates an independent copy.</summary>
    public SequenceSettings Clone() => new()
    {
        _start = _start,
        _step = _step,
        _padding = _padding,
        _style = _style,
        _groupSize = _groupSize
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
