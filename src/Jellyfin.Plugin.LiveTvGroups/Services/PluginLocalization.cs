using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.LiveTvGroups.Configuration;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Shared translations. Client display language and server library language are independent.</summary>
public static class PluginLocalization
{
    public static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Strings = Load();

    private static IReadOnlyDictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.LiveTvGroups.Localization.strings.json")
            ?? throw new InvalidOperationException("Missing translation resource.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }

    public static string Normalize(string? locale) => locale?.Split(',')[0].Split(';')[0].Trim().Split('-', '_')[0].ToLowerInvariant() == "de" ? "de" : "en";
    public static string Text(string key, string? locale)
        => Strings.TryGetValue(Normalize(locale), out var strings) && strings.TryGetValue(key, out var value) ? value : key;

    public static string ServerLanguage(IServiceProvider services)
        => services.GetService<IServerConfigurationManager>()?.Configuration.UICulture ?? "en-US";

    public static string Language(IServiceProvider services)
    {
        var header = services.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        return string.IsNullOrWhiteSpace(header) ? ServerLanguage(services) : header.Split(',')[0].Split(';')[0].Trim();
    }

    public static CultureInfo Culture(IServiceProvider services)
    {
        try { return CultureInfo.GetCultureInfo(Language(services)); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-US"); }
    }

    public static string DisplayName(PluginConfiguration? configuration, string? language)
        => string.IsNullOrWhiteSpace(configuration?.DisplayName) ? Text("Live-TV Groups", language) : configuration.DisplayName;
}