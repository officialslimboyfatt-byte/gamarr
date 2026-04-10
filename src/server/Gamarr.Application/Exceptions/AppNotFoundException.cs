namespace Gamarr.Application.Exceptions;

public sealed class AppNotFoundException(string message) : Exception(message);
