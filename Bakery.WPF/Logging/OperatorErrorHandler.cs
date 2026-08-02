using Serilog;

namespace Bakery.WPF.Logging;

public static class OperatorErrorHandler
{
    public static string LogAndTranslate(Exception exception, string operation)
    {
        Log.Error(exception, "Operator action failed: {Operation}", operation);
        return Bakery.Application.UserErrorMessages.FromException(exception);
    }
}
