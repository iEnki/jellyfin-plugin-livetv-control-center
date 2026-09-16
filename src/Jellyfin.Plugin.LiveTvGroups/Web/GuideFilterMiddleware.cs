using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.LiveTvGroups.Web;

/// <summary>
/// Adds <see cref="GuideFilterMiddleware"/> at the start of the request pipeline.
/// </summary>
public class GuideFilterStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<GuideFilterMiddleware>();
            next(app);
        };
    }
}

/// <summary>
/// Restricts <c>GET /LiveTv/Channels</c> to the user's active guide group, so the native program guide of TV apps
/// (Android TV / Fire TV, Wholphin, …) shows that group. Any failure passes the original response through.
/// </summary>
public class GuideFilterMiddleware
{
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string ClientClaim = "Jellyfin-Client";

    private readonly RequestDelegate _next;
    private readonly ILogger<GuideFilterMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuideFilterMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="logger">Logger.</param>
    public GuideFilterMiddleware(RequestDelegate next, ILogger<GuideFilterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var channelIds = await GetGroupChannelIdsAsync(context).ConfigureAwait(false);
        if (channelIds is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var query = context.Request.Query;
        int? startIndex = TryGetInt(query, "startIndex");
        int? limit = TryGetInt(query, "limit");

        // Page after filtering, and get an uncompressed body we can rewrite.
        context.Request.QueryString = QueryString.Create(query
            .Where(q => !q.Key.Equals("startIndex", StringComparison.OrdinalIgnoreCase) && !q.Key.Equals("limit", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v))));
        context.Request.Headers.AcceptEncoding = StringValues.Empty;

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var bytes = buffer.ToArray();
        if (context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
            && context.Response.Headers.ContentEncoding.Count == 0)
        {
            try
            {
                var filtered = GuideFilter.FilterChannels(Encoding.UTF8.GetString(bytes), channelIds, startIndex, limit);
                if (filtered is not null)
                {
                    bytes = Encoding.UTF8.GetBytes(filtered);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Guide filter could not rewrite the channel list; returning all channels");
            }
        }

        context.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static int? TryGetInt(IQueryCollection query, string key)
        => int.TryParse(query[key].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private async Task<IReadOnlyList<Guid>?> GetGroupChannelIdsAsync(HttpContext context)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null
                || !config.EnableGuideFilter
                || !HttpMethods.IsGet(context.Request.Method)
                || !context.Request.Path.Value!.EndsWith("/LiveTv/Channels", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var auth = await context.AuthenticateAsync().ConfigureAwait(false);
            var principal = auth.Principal;
            if (!auth.Succeeded || principal is null
                || !Guid.TryParse(principal.FindFirst(UserIdClaim)?.Value, out var userId)
                || GuideFilter.IsClientExcluded(principal.FindFirst(ClientClaim)?.Value, config.GuideFilterExcludedClients))
            {
                return null;
            }

            var groups = context.RequestServices.GetRequiredService<GroupService>();
            var doc = groups.Store.Get(userId);
            var group = doc.ActiveGuideGroupId is { } activeId ? doc.Groups.FirstOrDefault(g => g.Id == activeId) : null;
            var user = context.RequestServices.GetRequiredService<IUserManager>().GetUserById(userId);
            if (group is null || user is null)
            {
                return null;
            }

            return groups.ResolveChannels(user, group, groups.GetAccessibleChannels(user)).Select(c => c.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guide filter skipped");
            return null;
        }
    }
}
