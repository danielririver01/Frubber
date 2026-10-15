namespace FrubberPOS.Installer;

/// <summary>
/// JHO-95: error con el mensaje ya redactado para el usuario. El asistente lo
/// muestra tal cual, en vez del texto técnico de la excepción original, y
/// distingue en qué fase falló la instalación (copiar / accesos / registro).
/// </summary>
public sealed class InstallFailedException : Exception
{
    public InstallFailedException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
    }
}
