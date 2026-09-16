using System;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Web;

/// <summary>
/// State of the web client integration, shown on the dashboard page.
/// </summary>
public class WebInjectionStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the File Transformation plugin was found.
    /// </summary>
    public bool FileTransformationInstalled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the script injection is registered.
    /// </summary>
    public bool Registered { get; set; }

    /// <summary>
    /// Gets or sets the last error, if any.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Registers the index.html transformation with the File Transformation plugin.
/// </summary>
public class WebInjectionService : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("5f1c8f0e-3d0a-4c55-9b1e-6c2d4a7b8e01");

    private readonly WebInjectionStatus _status;
    private readonly ILogger<WebInjectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionService"/> class.
    /// </summary>
    /// <param name="status">Shared status.</param>
    /// <param name="logger">Logger.</param>
    public WebInjectionService(WebInjectionStatus status, ILogger<WebInjectionService> logger)
    {
        _status = status;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableWebIntegration != true)
        {
            _logger.LogInformation("Live-TV Groups web integration is disabled");
            return Task.CompletedTask;
        }

        try
        {
            // File Transformation is loaded into its own load context and cannot be referenced directly.
            var assembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

            _status.FileTransformationInstalled = assembly is not null;
            if (assembly is null)
            {
                _status.Error = "Plugin \"File Transformation\" ist nicht installiert.";
                _logger.LogWarning("File Transformation plugin not found; Live-TV Groups web integration is not available");
                return Task.CompletedTask;
            }

            var register = assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?.GetMethod("RegisterTransformation");
            var payloadType = register?.GetParameters().FirstOrDefault()?.ParameterType;
            var parse = payloadType?.GetMethod("Parse", [typeof(string)]);
            if (register is null || parse is null)
            {
                _status.Error = "Nicht unterstützte Version von \"File Transformation\".";
                _logger.LogWarning("Unsupported File Transformation plugin version");
                return Task.CompletedTask;
            }

            var json = JsonSerializer.Serialize(new
            {
                id = TransformationId,
                fileNamePattern = IndexTransformer.FileNamePattern,
                callbackAssembly = typeof(WebInjectionService).Assembly.FullName,
                callbackClass = typeof(IndexTransformer).FullName,
                callbackMethod = nameof(IndexTransformer.Transform)
            });

            register.Invoke(null, [parse.Invoke(null, [json])]);
            _status.Registered = true;
            _status.Error = null;
            _logger.LogInformation("Live-TV Groups web integration registered");
        }
        catch (Exception ex)
        {
            _status.Error = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError(ex, "Failed to register Live-TV Groups web integration");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
