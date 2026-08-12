using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.SubtitleFontBridge.Models;
using Jellyfin.Plugin.SubtitleFontBridge.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SkiaSharp;

namespace Jellyfin.Plugin.SubtitleFontBridge.Api;

/// <summary>
/// Resolves and streams fonts visible to the Jellyfin server process.
/// </summary>
[ApiController]
[Route(Plugin.ApiRoute)]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SubtitleFontBridgeController : ControllerBase
{
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ILibraryManager _libraryManager;
    private readonly ISubtitleFontResolver _subtitleFontResolver;
    private readonly ISystemFontCatalog _fontCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleFontBridgeController"/> class.
    /// </summary>
    public SubtitleFontBridgeController(
        IAuthorizationContext authorizationContext,
        ILibraryManager libraryManager,
        ISubtitleFontResolver subtitleFontResolver,
        ISystemFontCatalog fontCatalog)
    {
        _authorizationContext = authorizationContext;
        _libraryManager = libraryManager;
        _subtitleFontResolver = subtitleFontResolver;
        _fontCatalog = fontCatalog;
    }

    /// <summary>
    /// Resolves every font family referenced by one ASS/SSA subtitle stream.
    /// </summary>
    /// <response code="200">The font resolution result.</response>
    /// <response code="401">No Jellyfin user is associated with the request.</response>
    /// <response code="404">The item is absent or inaccessible to the current user.</response>
    /// <response code="413">The subtitle exceeds the analysis limit.</response>
    [HttpGet("Subtitles/{itemId:guid}/{mediaSourceId}/{subtitleIndex:int}")]
    [ProducesResponseType<SubtitleFontResolutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<SubtitleFontResolutionDto>> ResolveSubtitleFonts(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] string mediaSourceId,
        [FromRoute, Required] int subtitleIndex,
        CancellationToken cancellationToken)
    {
        var authorization = await _authorizationContext
            .GetAuthorizationInfo(HttpContext)
            .ConfigureAwait(false);
        if (authorization.User is null)
        {
            return Unauthorized();
        }

        var item = _libraryManager.GetItemById<BaseItem>(itemId, authorization.User);
        if (item is null)
        {
            return NotFound();
        }

        try
        {
            var resolution = await _subtitleFontResolver.ResolveAsync(
                item,
                mediaSourceId,
                subtitleIndex,
                cancellationToken).ConfigureAwait(false);

            return Ok(new SubtitleFontResolutionDto(
                itemId,
                mediaSourceId,
                subtitleIndex,
                resolution));
        }
        catch (InvalidDataException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "The subtitle cannot be analyzed.",
                detail: exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ResourceNotFoundException)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The requested subtitle stream was not found.",
                detail: exception.Message);
        }
    }

    /// <summary>
    /// Resolves explicit font family names without reading a subtitle.
    /// </summary>
    /// <response code="200">The font resolution result.</response>
    /// <response code="400">The request exceeds the family count or name limits.</response>
    [HttpPost("Resolve")]
    [ProducesResponseType<SystemFontResolutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<SystemFontResolutionDto> ResolveFonts(
        [FromBody, Required] ResolveFontsRequest request)
    {
        try
        {
            return Ok(_fontCatalog.ResolveFamilies(request.Families));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The font resolution request is invalid.",
                Detail = exception.Message
            });
        }
    }

    /// <summary>
    /// Streams one previously resolved font file.
    /// </summary>
    /// <response code="200">The font file.</response>
    /// <response code="404">The opaque id or extension is unknown.</response>
    [HttpGet("Files/{fontId}.{extension}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("font/ttf", "font/otf", "font/collection", "font/woff", "font/woff2", MediaTypeNames.Application.Octet)]
    public ActionResult GetFont(
        [FromRoute, Required] string fontId,
        [FromRoute, Required] string extension)
    {
        if (!_fontCatalog.TryOpenFont(fontId, out var resource))
        {
            return NotFound();
        }

        var expectedExtension = Path.GetExtension(resource.FileName).TrimStart('.');
        if (!extension.Equals(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            resource.Stream.Dispose();
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return new FileStreamResult(resource.Stream, resource.ContentType)
        {
            EnableRangeProcessing = true,
            EntityTag = new EntityTagHeaderValue('"' + resource.Id + '"')
        };
    }

    /// <summary>
    /// Gets administrative diagnostics for the font catalog.
    /// </summary>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<SystemFontStatusDto>(StatusCodes.Status200OK)]
    public ActionResult<SystemFontStatusDto> GetStatus()
    {
        var version = typeof(SKFontManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        return Ok(new SystemFontStatusDto(
            _fontCatalog.FontFamilyCount > 0,
            RuntimeInformation.OSDescription,
            _fontCatalog.FontFamilyCount,
            _fontCatalog.CachedFamilyCount,
            _fontCatalog.IndexedFileCount,
            version));
    }
}
