using FhirMcp.Core.Auth;
using FhirMcp.Core.Fhir;
using Xunit;

namespace FhirMcp.Tests;

public class FhirServerSettingsTests
{
    private static FhirServerSettings Read(params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);
        return FhirServerSettings.FromEnvironment(k => map.GetValueOrDefault(k));
    }

    [Fact]
    public void Defaults_to_the_SMART_sandbox()
    {
        var server = Read();
        Assert.Equal("smart", server.Preset!.Name);
        Assert.Equal("https://r4.smarthealthit.org/", server.BaseUrl);
    }

    [Theory]
    [InlineData("hapi", "https://hapi.fhir.org/baseR4/")]
    [InlineData(" Smart-Login ", FhirServerSettings.SmartLauncherUrl)]
    public void Picks_a_server_by_name(string name, string expected)
    {
        Assert.Equal(expected, Read((FhirServerSettings.ServerVariable, name)).BaseUrl);
    }

    [Fact]
    public void Accepts_any_other_server_by_url_and_adds_the_trailing_slash()
    {
        var server = Read((FhirServerSettings.BaseUrlVariable, "https://fhir.example/r4"));
        Assert.Null(server.Preset);
        Assert.Equal("https://fhir.example/r4/", server.BaseUrl);
    }

    [Fact]
    public void Refuses_both_settings_an_unknown_name_and_a_bad_url()
    {
        Assert.Contains("not both", Assert.Throws<ArgumentException>(() =>
            Read((FhirServerSettings.ServerVariable, "hapi"), (FhirServerSettings.BaseUrlVariable, "https://x/"))).Message);
        Assert.Contains("smart, hapi, smart-login", Assert.Throws<ArgumentException>(() =>
            Read((FhirServerSettings.ServerVariable, "epic"))).Message);
        Assert.Throws<ArgumentException>(() => Read((FhirServerSettings.BaseUrlVariable, "ftp://x/")));
    }

    [Fact]
    public void Only_the_sign_in_preset_turns_on_sign_in_with_a_patient_scope()
    {
        var presets = FhirServerSettings.Presets.ToDictionary(p => p.Name);
        Assert.False(presets["smart"].SignIn);
        Assert.False(presets["hapi"].SignIn);
        Assert.True(presets["smart-login"].SignIn);

        var preset = presets["smart-login"];
        SmartSettings Smart(params (string Key, string Value)[] vars)
        {
            var map = vars.ToDictionary(v => v.Key, v => v.Value);
            return SmartSettings.FromEnvironment(k => map.GetValueOrDefault(k), "/home/test", false, preset.SignIn, preset.DefaultScope);
        }

        var defaults = Smart();
        Assert.True(defaults.Enabled);
        Assert.Contains("patient/*.read", defaults.Scope);

        // Explicit settings still win over the preset.
        Assert.False(Smart((SmartSettings.AuthVariable, "none")).Enabled);
        Assert.Equal("user/*.read", Smart((SmartSettings.ScopeVariable, "user/*.read")).Scope);
    }
}
