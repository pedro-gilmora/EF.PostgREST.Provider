using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PosgREST.DbContext.Provider.Core.Diagnostics;

/// <summary>
/// Logging definitions for the PostgREST provider.
/// PostgREST is a stateless HTTP provider so there are no
/// provider-specific logging events beyond the core EF ones.
/// </summary>
public class PostgRestLoggingDefinitions : LoggingDefinitions;
