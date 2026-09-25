using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LaserWorks.Desktop.ViewModels;

namespace LaserWorks.Desktop;

/// <summary>Resolves XxxViewModel → XxxView by convention.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public static ViewLocator Instance { get; } = new();
    private static readonly Dictionary<Type, Type?> Cache = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;
        var vmType = data.GetType();
        if (!Cache.TryGetValue(vmType, out var viewType))
        {
            var name = vmType.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            viewType = vmType.Assembly.GetType(name);
            Cache[vmType] = viewType;
        }
        if (viewType == null) return new TextBlock { Text = "View not found: " + vmType.Name };
        return (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
