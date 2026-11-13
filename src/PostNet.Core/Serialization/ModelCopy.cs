using System.Text.Json;
using PostNet.Core.Models;

namespace PostNet.Core.Serialization;

public static class ModelCopy
{
    public static RequestDefinition Request(RequestDefinition request) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(request, AppJsonContext.Default.RequestDefinition), AppJsonContext.Default.RequestDefinition)!;
}
