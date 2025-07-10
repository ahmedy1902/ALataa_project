using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

public class AuthorizeFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var path = context.HttpContext.Request.Path.ToString().ToLower();

        var excludedPaths = new[]
        {
            "/account/login",
            "/account/register",
            "/account/accessdenied"
        };

        if (!context.HttpContext.User.Identity.IsAuthenticated &&
            !excludedPaths.Contains(path))
        {
            context.Result = new RedirectToRouteResult(new RouteValueDictionary
            {
                { "controller", "Account" },
                { "action", "Login" }
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
