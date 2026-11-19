
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UrlShortener.Api.Filters
{
    public class AuthorizeCheckOperationFilter : IOperationFilter
    {
        public void Apply(
            OpenApiOperation operation,
            OperationFilterContext context)
        {
            var hasAllowAnonymous = context.ApiDescription.ActionDescriptor
                .EndpointMetadata
                .OfType<IAllowAnonymous>()
                .Any();

            if (hasAllowAnonymous)
                return;

            var hasAuthorize = context.ApiDescription.ActionDescriptor
                .EndpointMetadata
                .OfType<IAuthorizeData>()
                .Any();

            if (!hasAuthorize)
                return;

            operation.Security ??= [];

            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("ApiKey", context.Document)] = []
                });

            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                });
        }
    }
}
