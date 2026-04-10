namespace Gamarr.Application.Exceptions;

public sealed class AppValidationException(string message) : Exception(message);
