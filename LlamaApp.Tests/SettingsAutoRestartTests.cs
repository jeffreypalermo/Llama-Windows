using System.Text.Json;
using Xunit;

namespace LlamaApp.Tests;

/// <summary>
/// The auto-restart opt-in is off unless the user turns it on, including for
/// a settings.json written before the setting existed.
/// </summary>
public sealed class SettingsAutoRestartTests
{
    [Fact]
    public void AutoRestart_DefaultsToOff()
    {
        var settings = new Settings();

        Assert.False(settings.AutoRestartServer);
    }

    [Fact]
    public void AutoRestart_MissingFromSavedJson_StaysOff()
    {
        const string json = """{"ServerPort":9931,"LaunchAtStartup":true}""";

        var settings = JsonSerializer.Deserialize<Settings>(json);

        Assert.NotNull(settings);
        Assert.False(settings.AutoRestartServer);
    }

    [Fact]
    public void ProductionDeserializer_LeavesAutoRestartOff_WhenTheFieldIsMissing()
    {
        const string json = """{"ServerPort":9931}""";

        var settings = JsonSerializer.Deserialize(
            json, SettingsJsonContext.Default.Settings);

        Assert.NotNull(settings);
        Assert.False(settings.AutoRestartServer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoRestart_RoundTripsThroughJson(bool enabled)
    {
        var settings = new Settings { AutoRestartServer = enabled };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<Settings>(json);

        Assert.NotNull(loaded);
        Assert.Equal(enabled, loaded.AutoRestartServer);
        Assert.Contains("\"AutoRestartServer\"", json);
    }
}
