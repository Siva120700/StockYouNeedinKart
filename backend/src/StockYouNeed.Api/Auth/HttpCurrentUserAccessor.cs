using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;

namespace StockYouNeed.Api.Auth;

public sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    public Guid UserId { get; }

    public HttpCurrentUserAccessor(IHttpContextAccessor http, Microsoft.Extensions.Options.IOptions<DevAuthOptions> options)
    {
        var ctx = http.HttpContext;
        if (ctx?.Request.Headers.TryGetValue("X-User-Id", out var header) == true
            && Guid.TryParse(header.ToString(), out var fromHeader))
        {
            UserId = fromHeader;
            return;
        }

        UserId = options.Value.DemoUserId;
    }
}
