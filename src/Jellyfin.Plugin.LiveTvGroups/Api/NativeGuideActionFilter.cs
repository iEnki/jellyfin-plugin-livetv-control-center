using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>Activates authenticated group or native guide-folder visits on the original TV client, including cached provider results.</summary>
public sealed class NativeGuideActionFilter(NativeGuideActionService actions, ILogger<NativeGuideActionFilter> logger)
    : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -800;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var type = descriptor?.ControllerTypeInfo.FullName;
        var isItems = type == "Jellyfin.Api.Controllers.ItemsController" && descriptor?.ActionName is "GetItems" or "GetItemsByUserIdLegacy";
        var isChannels = type == "Jellyfin.Api.Controllers.ChannelsController" && descriptor?.ActionName == "GetChannelItems";
        var user = context.HttpContext.User;
        if ((!isItems && !isChannels) || user.Identity?.IsAuthenticated != true
            || user.FindFirst("Jellyfin-IsApiKey")?.Value != "False"
            || !PlayerService.IsAndroidTvClient(user.FindFirst("Jellyfin-Client")?.Value)
            || !Guid.TryParse(user.FindFirst("Jellyfin-UserId")?.Value, out var userId)
            || (context.ActionArguments.TryGetValue("userId", out var requested) && requested is Guid id && id != userId)
            || (context.ActionArguments.TryGetValue("recursive", out var recursive) && recursive is true)
            || (context.ActionArguments.TryGetValue("searchTerm", out var search) && search is string term && !string.IsNullOrWhiteSpace(term))
            || (context.ActionArguments.TryGetValue("filters", out var filters) && filters is ICollection { Count: > 0 })
            || !context.ActionArguments.TryGetValue(isItems ? "parentId" : "folderId", out var folder) || folder is not Guid parentId)
        { await next().ConfigureAwait(false); return; }
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null || executed.Canceled || executed.Result is not ObjectResult result
            || (result.StatusCode is int status && status != 200) || result.Value is not QueryResult<BaseItemDto> items) return;
        try
        {
            Guid? channel = isChannels && context.ActionArguments.TryGetValue("channelId", out var requestedChannel)
                && requestedChannel is Guid channelId ? channelId : null;
            var applied = await actions.OpenAsync(context.HttpContext, parentId, channel).ConfigureAwait(false);
            if (applied is null) return;
            if (applied.DirectGroupEntry)
            {
                // The provider keeps media children for playlist synchronization. The authenticated
                // TV response shows only the guide choices if navigation was not rendered.
                result.Value = new QueryResult<BaseItemDto>(items.Items.Where(item => item.IsFolder == true).ToArray())
                {
                    TotalRecordCount = Math.Min(items.TotalRecordCount, 2),
                    StartIndex = items.StartIndex
                };
            }
            if (applied.DirectGroupEntry || items.Items.Count != 1) return;
            var language = context.HttpContext.Request.Headers.AcceptLanguage.ToString();
            var hint = items.Items[0];
            hint.Name = applied.GroupId is null ? PluginLocalization.Text("All channels selected for this TV", language)
                : PluginLocalization.Text("Group selected for this TV: ", language) + applied.GroupName;
            hint.Overview = PluginLocalization.Text("Open Live TV → TV Guide", language) + ". "
                + PluginLocalization.Text("The native TV guide opens in ordinary Live TV. Select TV Guide there. Reopen the app if its old channel list remains cached.", language);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Native guide TV action could not be completed. Existing guide and folder fallback retained."); }
    }
}
