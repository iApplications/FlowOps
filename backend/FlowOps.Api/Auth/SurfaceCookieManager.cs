using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowOps.Api.Auth;

// The selector chooses a cookie jar; permissions still check the authenticated account.
public sealed class SurfaceCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager inner = new();
    private static string Name(HttpContext context, string key) =>
        key + "." + AuthEndpoints.NormalizeSurface(context.Request.Headers["X-FlowOps-Surface"].ToString());

    public string? GetRequestCookie(HttpContext context, string key) => inner.GetRequestCookie(context, Name(context, key));
    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        inner.AppendResponseCookie(context, Name(context, key), value, options);
    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        inner.DeleteCookie(context, Name(context, key), options);
}
