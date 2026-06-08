using System.Text.Json.Serialization;

namespace ChocoboRunner.Models.GithubModels;

[JsonSerializable(typeof(ReleaseModel))]
internal partial class ReleaseModelContext : JsonSerializerContext
{
}