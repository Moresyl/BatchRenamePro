using System.ComponentModel;

namespace BatchRenamePro.Core.Abstractions;

/// <summary>
/// One composable step in the rename pipeline. Rules are pure with respect to the file system:
/// they transform a name and never touch disk.
/// </summary>
/// <remarks>
/// Rules raise <see cref="INotifyPropertyChanged.PropertyChanged"/> so the shell can rebuild the
/// live preview the moment any rule setting changes, without the UI having to know which knob moved.
/// </remarks>
public interface IRenameRule : INotifyPropertyChanged
{
    /// <summary>Stable discriminator used for preset serialization. Never localize or rename this.</summary>
    string TypeKey { get; }

    /// <summary>Whether the rule participates in the pipeline.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Transforms a name. Must not throw for ordinary bad input; report that from <see cref="Validate"/>.</summary>
    /// <param name="input">The name produced by the previous rule in the pipeline.</param>
    /// <param name="context">Facts about the item being renamed.</param>
    NameParts Apply(NameParts input, RenameContext context);

    /// <summary>Returns every problem with the rule's current settings.</summary>
    IReadOnlyList<RuleDiagnostic> Validate();

    /// <summary>Creates an independent copy, used for presets, undo of rule edits and preview isolation.</summary>
    IRenameRule Clone();
}
