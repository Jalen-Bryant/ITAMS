using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ITAMS.Api.Swagger;

public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = endpointMetadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = endpointMetadata.OfType<IAuthorizeData>().Any();

        if (allowsAnonymous || !requiresAuthorization)
        {
            // Explicitly mark anonymous operations so Swagger does not attach the bearer scheme to login or refresh requests.
            operation.Security = [];
            return;
        }

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Unauthorized"
        });
        operation.Responses.TryAdd("403", new OpenApiResponse
        {
            Description = "Forbidden"
        });
    }
}
