using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>Applies an explicitly selected group to the original TV channel DTOs before serialization.</summary>
public class NativeGuideChannelFilter(NativeGuideService scopes, IUserManager users,
    ILogger<NativeGuideChannelFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    // The central channel-access filter surrounds this filter and keeps its normal security checks.
    public int Order => -900;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var principal = context.HttpContext.User;
        var device = principal.FindFirst("Jellyfin-DeviceId")?.Value;
        if (descriptor?.ControllerTypeInfo.FullName != "Jellyfin.Api.Controllers.LiveTvController"
            || descriptor.ActionName != "GetLiveTvChannels"
            || principal.Identity?.IsAuthenticated != true
            || !PlayerService.IsAndroidTvClient(principal.FindFirst("Jellyfin-Client")?.Value)
            || !string.Equals(principal.FindFirst("Jellyfin-IsApiKey")?.Value, "False", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(device)
            || !Guid.TryParse(principal.FindFirst("Jellyfin-UserId")?.Value, out var userId) || userId == Guid.Empty
            || (context.ActionArguments.TryGetValue("userId", out var requested) && requested is Guid requestedId && requestedId != userId))
        { await next().ConfigureAwait(false); return; }

        var active = false;
        try
        {
            var observed = scopes.GetSelection(userId, device);
            if (observed is not null && users.GetUserById(userId) is { } user)
            {
                if (scopes.Resolve(user, observed) is null)
                {
                    scopes.ClearSelection(userId, device, observed);
                    logger.LogDebug("Native guide scope invalid for user {UserId}, device {DeviceId}; ordinary Live TV retained.", userId, device);
                }
                else active = true;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Native guide scope validation failed for user {UserId}, device {DeviceId}; ordinary Live TV retained.", userId, device);
        }
        if (!active) { await next().ConfigureAwait(false); return; }

        // Core has no channelIds argument on this action. Obtain its full, sorted, authorized result,
        // intersect it and only then paginate. Preserve every other core argument/DTO option.
        var hasStart = context.ActionArguments.TryGetValue("startIndex", out var originalStart);
        var hasLimit = context.ActionArguments.TryGetValue("limit", out var originalLimit);
        // MVC omits unbound nullable arguments. Supply null so filtering also works on the first/all-channel request.
        context.ActionArguments["startIndex"] = null;
        context.ActionArguments["limit"] = null;
        ActionExecutedContext executed;
        try { executed = await next().ConfigureAwait(false); }
        finally
        {
            if (hasStart) context.ActionArguments["startIndex"] = originalStart; else context.ActionArguments.Remove("startIndex");
            if (hasLimit) context.ActionArguments["limit"] = originalLimit; else context.ActionArguments.Remove("limit");
        }
        if (executed.Exception is not null || executed.Canceled
            || executed.Result is not ObjectResult result || (result.StatusCode is int status && status != 200)
            || result.Value is not QueryResult<BaseItemDto> channels) return;

        var items = channels.Items.ToArray();
        var total = channels.TotalRecordCount;
        try
        {
            // Re-read after the action: reset, replacement, deletion and permission changes may race it.
            var current = scopes.GetSelection(userId, device);
            var user = users.GetUserById(userId);
            var allowed = current is null || user is null ? null : scopes.Resolve(user, current);
            if (current is not null && allowed is null) scopes.ClearSelection(userId, device, current);
            if (allowed is not null)
            {
                items = items.Where(item => allowed.Contains(item.Id)).ToArray();
                total = items.Length;
                logger.LogDebug("Native guide filtered user {UserId}, device {DeviceId}, selection {Selection}: {Count} channels.", userId, device, current, total);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Native guide filtering failed for user {UserId}, device {DeviceId}; ordinary Live TV retained.", userId, device); }

        var start = originalStart is int startIndex ? Math.Max(0, startIndex) : 0;
        var page = items.Skip(start);
        if (originalLimit is int limit) page = page.Take(Math.Max(0, limit));
        // Keep status/content-type/formatter metadata on the original ObjectResult.
        result.Value = new QueryResult<BaseItemDto>(originalStart as int?, total, page.ToArray());
    }
}
