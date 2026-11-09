using System.Text.Json.Serialization;
using ApiNet.Core.Models;

namespace ApiNet.Core.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkspaceDocument))]
[JsonSerializable(typeof(RequestDefinition))]
[JsonSerializable(typeof(List<RequestParameter>))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(List<VariableDefinition>))]
public partial class AppJsonContext : JsonSerializerContext;
