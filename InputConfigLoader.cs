using System.Text.Json;

namespace RaylibGameFramework.Input;

public static class InputConfigLoader
{
    public static InputConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string resolvedPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
        string json = File.ReadAllText(resolvedPath);

        InputConfig config = JsonSerializer.Deserialize<InputConfig>(json)
            ?? throw new InvalidDataException($"Input configuration '{resolvedPath}' must contain a JSON object.");

        if (config.Bindings is null)
        {
            throw new InvalidDataException($"Input configuration '{resolvedPath}' must contain a Bindings array.");
        }

        return config;
    }
}
