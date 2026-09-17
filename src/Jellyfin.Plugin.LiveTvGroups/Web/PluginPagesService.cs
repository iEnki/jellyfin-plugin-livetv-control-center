using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Web;

/// <summary>Optional user-menu entry. The channel route remains available without Plugin Pages.</summary>
public class PluginPagesService(IServiceProvider services, WebInjectionStatus status, ILogger<PluginPagesService> logger) : IHostedService
{
    private const string PageId = "livetv-groups";
    private MethodInfo? _remove;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var assembly = AssemblyLoadContext.All.SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.PluginPages");
        status.PluginPagesInstalled = assembly is not null;
        if (assembly is null || Plugin.Instance?.Configuration.EnableWebIntegration != true || !status.Registered)
        {
            return Task.CompletedTask;
        }
        try
        {
            var type = assembly.GetType("Jellyfin.Plugin.PluginPages.PluginInterface");
            var register = type?.GetMethod("RegisterPage");
            var parse = register?.GetParameters().FirstOrDefault()?.ParameterType.GetMethod("Parse", [typeof(string)]);
            _remove = type?.GetMethod("RemovePage", [typeof(string)]);
            if (register is null || parse is null || _remove is null)
            {
                status.PluginPagesError = "Unsupported Plugin Pages version.";
                return Task.CompletedTask;
            }
            var json = JsonSerializer.Serialize(new
            {
                id = PageId, url = "/LiveTvGroups/page.html", displayText = PluginLocalization.DisplayName(Plugin.Instance?.Configuration, PluginLocalization.ServerLanguage(services)), icon = "live_tv",
                isEnabledAssembly = typeof(PluginPagesService).Assembly.FullName,
                isEnabledClass = nameof(PluginPagesService), isEnabledMethod = nameof(IsEnabled)
            });
            _remove.Invoke(null, [PageId]);
            register.Invoke(null, [parse.Invoke(null, [json])]);
            status.PluginPagesRegistered = true;
        }
        catch (Exception ex)
        {
            status.PluginPagesError = "Could not register the menu entry.";
            logger.LogWarning(ex, "Optional Plugin Pages registration failed; the groups channel remains available");
        }
        return Task.CompletedTask;
    }

    public static bool IsEnabled(string pageId) => pageId == PageId && Plugin.Instance?.Configuration.EnableWebIntegration == true;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (status.PluginPagesRegistered)
        {
            try { _remove?.Invoke(null, [PageId]); }
            catch (Exception ex) { logger.LogWarning(ex, "Optional Plugin Pages entry removal failed"); }
            status.PluginPagesRegistered = false;
        }
        return Task.CompletedTask;
    }
}
