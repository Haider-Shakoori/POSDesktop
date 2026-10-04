using System.Reflection;
using System.Text.Json;

namespace BusinessOS.POS.LocalClient;

public sealed class LanRpcProxy<T> : DispatchProxy where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private LanApiClient _api = null!;
    private string _service = string.Empty;

    public static T Create(LanApiClient api, string service)
    {
        var proxy = Create<T, LanRpcProxy<T>>();
        var typed = (LanRpcProxy<T>)(object)proxy;
        typed._api = api;
        typed._service = service;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) throw new ArgumentNullException(nameof(targetMethod));
        args ??= [];

        var parameters = targetMethod.GetParameters();
        var cancellationToken = CancellationToken.None;
        var payload = new List<JsonElement>();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                cancellationToken = args[i] is CancellationToken ct ? ct : CancellationToken.None;
                continue;
            }
            payload.Add(JsonSerializer.SerializeToElement(args[i], parameters[i].ParameterType, JsonOptions));
        }

        if (targetMethod.ReturnType == typeof(Task))
            return InvokeVoidAsync(targetMethod.Name, payload, cancellationToken);

        if (targetMethod.ReturnType.IsGenericType &&
            targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(LanRpcProxy<T>)
                .GetMethod(nameof(InvokeGenericAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(resultType)
                .Invoke(this, [targetMethod.Name, payload, cancellationToken])!;
        }

        throw new NotSupportedException("LAN RPC supports Task and Task<T> interface methods only.");
    }

    private Task InvokeVoidAsync(string method, IReadOnlyList<JsonElement> args, CancellationToken cancellationToken) =>
        _api.PostAsync($"rpc/{_service}/{method}", new { arguments = args }, true, true, cancellationToken);

    private Task<TResult> InvokeGenericAsync<TResult>(
        string method, IReadOnlyList<JsonElement> args, CancellationToken cancellationToken) =>
        _api.PostAsync<TResult>($"rpc/{_service}/{method}", new { arguments = args }, true, true, cancellationToken);
}
