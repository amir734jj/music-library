using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MusicLibrary.Contracts.Constants;

[JsonConverter(typeof(StringEnumConverter))]
public enum AuthenticationResultType
{
    Login,
    Registration
}