namespace AlexIsland.Core.Security;

/// <summary>
/// Decide qué enlaces de reunión se pueden abrir con el lanzador del sistema.
/// </summary>
/// <remarks>
/// Los eventos pueden venir de archivos .ics, del portapapeles o de una URL
/// descargada, es decir, de fuentes no confiables. Abrir sin filtrar un valor
/// como <c>file://</c>, <c>search-ms:</c>, <c>ms-msdt:</c> o cualquier protocolo
/// registrado en el equipo permitiría a un calendario malicioso ejecutar
/// acciones locales con un solo clic en "Unirse". Por eso solo se aceptan
/// enlaces <c>https</c> hacia servicios de videollamada conocidos.
/// </remarks>
public static class MeetingUrlPolicy
{
    /// <summary>Longitud máxima aceptada para un enlace de reunión.</summary>
    public const int MaxLength = 2048;

    // Hosts exactos permitidos.
    private static readonly string[] ExactHosts =
    [
        "meet.google.com",
        "teams.microsoft.com",
        "teams.live.com",
        "zoom.us",
        "webex.com",
    ];

    // Dominios cuyos subdominios están permitidos (por ejemplo, empresa.zoom.us).
    private static readonly string[] SubdomainParents =
    [
        "zoom.us",
        "webex.com",
    ];

    /// <summary>
    /// Devuelve <c>true</c> y el <see cref="Uri"/> normalizado si el texto es un
    /// enlace de reunión seguro de abrir.
    /// </summary>
    public static bool TryGetSafeUri(string? candidate, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var trimmed = candidate.Trim();
        if (trimmed.Length > MaxLength) return false;

        // Caracteres de control o espacios no pueden aparecer en un enlace válido.
        foreach (var c in trimmed)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

        // Rechaza credenciales embebidas (https://usuario@host) y puertos no estándar.
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;
        if (!parsed.IsDefaultPort) return false;
        if (parsed.HostNameType != UriHostNameType.Dns) return false;

        if (!IsAllowedHost(parsed.IdnHost)) return false;

        uri = parsed;
        return true;
    }

    /// <summary>Devuelve el enlace normalizado si es seguro, o <c>null</c>.</summary>
    public static string? Sanitize(string? candidate)
        => TryGetSafeUri(candidate, out var uri) ? uri.AbsoluteUri : null;

    /// <summary>Indica si el host pertenece a un servicio de videollamada permitido.</summary>
    public static bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var normalized = host.TrimEnd('.').ToLowerInvariant();

        foreach (var exact in ExactHosts)
        {
            if (normalized == exact) return true;
        }

        foreach (var parent in SubdomainParents)
        {
            if (normalized.EndsWith("." + parent, StringComparison.Ordinal))
            {
                // Solo etiquetas DNS válidas; evita hosts como "a..zoom.us".
                var prefix = normalized[..^(parent.Length + 1)];
                if (prefix.Length > 0 && prefix.Split('.').All(IsValidDnsLabel)) return true;
            }
        }

        return false;
    }

    private static bool IsValidDnsLabel(string label)
    {
        if (label.Length is 0 or > 63) return false;
        if (label[0] == '-' || label[^1] == '-') return false;
        return label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }
}
