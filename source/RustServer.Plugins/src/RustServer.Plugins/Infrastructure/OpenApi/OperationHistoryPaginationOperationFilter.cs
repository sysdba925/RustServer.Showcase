using Microsoft.OpenApi;
using RustServer.Plugins.Features.PluginManagement.Operations;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace RustServer.Plugins.Infrastructure.OpenApi;

/** Documents the compact operation-history query bounds without changing its error envelope. */
public sealed class OperationHistoryPaginationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.RelativePath, "api/operations", StringComparison.Ordinal))
        {
            return;
        }

        var limit = operation.Parameters?.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, "limit", StringComparison.Ordinal));
        if (limit?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Minimum = OperationHistoryPagination.MinimumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        schema.Maximum = OperationHistoryPagination.MaximumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        schema.Default = JsonValue.Create(OperationHistoryPagination.DefaultLimit);
    }
}
