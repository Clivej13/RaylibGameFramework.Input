namespace RaylibGameFramework.Input;

public sealed class InputConfig
{
    public List<InputBinding> Bindings { get; init; } = [];
}

public sealed class InputBinding
{
    public string Device { get; init; } = string.Empty;

    public string Input { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;
}
