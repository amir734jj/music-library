using JsonSubTypes;
using MusicLibrary.Contracts.Constants;
using Newtonsoft.Json;

namespace MusicLibrary.Contracts.Responses;

[JsonConverter(typeof(JsonSubtypes), nameof(Type))]
[JsonSubtypes.KnownSubType(typeof(LoginAuthenticationResult), AuthenticationResultType.Login)]
[JsonSubtypes.KnownSubType(typeof(RegistrationAuthenticationResult), AuthenticationResultType.Registration)]
public abstract record AuthenticationResult(AuthenticationResultType Type, UserSummary User);