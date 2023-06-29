using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Lambda.Logging.AspNetCore.JsonConverters;

namespace Microsoft.Extensions.Logging
{
	internal class LambdaILogger : ILogger
	{
		// Private fields
		private readonly string _categoryName;
		private readonly LambdaLoggerOptions _options;

        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions()
        {
            Converters =
			{
				new JsonStringEnumConverter(),
				new JsonExceptionConverter(),
			},
        };

        internal IExternalScopeProvider ScopeProvider { get; set; }

		// Constructor
		public LambdaILogger(string categoryName, LambdaLoggerOptions options)
		{
			_categoryName = categoryName;
			_options = options;
		}

		// ILogger methods
		public IDisposable BeginScope<TState>(TState state) => ScopeProvider?.Push(state) ?? new NoOpDisposable();

		public bool IsEnabled(LogLevel logLevel)
		{
			return (
				_options.Filter == null ||
				_options.Filter(_categoryName, logLevel));
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			if (formatter == null)
			{
				throw new ArgumentNullException(nameof(formatter));
			}

			if (!IsEnabled(logLevel))
			{
				return;
			}

			// Format of the logged text, optional components are in {}
			//  {[LogLevel] }{ => Scopes : }{Category: }{EventId: }MessageText {Exception}{\n}

			var components = new List<string>(4);
			if (_options.IncludeLogLevel)
			{
				components.Add($"[{logLevel}]");
			}

			GetScopeInformation(components);

			if (_options.IncludeCategory)
			{
				components.Add($"{_categoryName}:");
			}
			if (_options.IncludeEventId)
			{
				components.Add($"[{eventId}]:");
			}

			var text = formatter.Invoke(state, exception);
			components.Add(text);

			if (_options.IncludeException)
			{
				components.Add($"{exception}");
			}

			if (_options.IncludeState && state is IEnumerable<KeyValuePair<string, object>> structuredLogData)
			{
				try
				{
					var serializableState = structuredLogData
                        // A lot of Microsoft types are not serializable
                        .Where(kv => !kv.Value.GetType().Namespace?.StartsWith(nameof(Microsoft)) ?? false)
						.Concat(new[]
						{
							// We always want to add the exception to the serialized state to preserve exception details
							new KeyValuePair<string, object>(nameof(Exception), exception)
						})
						.Where(kv => kv.Value != null)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
					var serializedState = JsonSerializer.Serialize(serializableState, options: _serializerOptions);
					components.Add(serializedState);
				}
				catch
				{
					// If state is not serializable, we skip it (could be complex objects coming from consumers, using pointers or other incompatible types)
				}
			}

			if (_options.IncludeNewline)
			{
				components.Add(Environment.NewLine);
			}

			var finalText = string.Join(" ", components);
			Amazon.Lambda.Core.LambdaLogger.Log(finalText);
		}

		private void GetScopeInformation(List<string> logMessageComponents)
		{
			var scopeProvider = ScopeProvider;

			if (_options.IncludeScopes && scopeProvider != null)
			{
				var initialCount = logMessageComponents.Count;

				scopeProvider.ForEachScope((scope, list) =>
				{
					list.Add(scope.ToString());
				}, (logMessageComponents));

				if (logMessageComponents.Count > initialCount)
				{
					logMessageComponents.Add("=>");
				}
			}
		}

		// Private classes	       
		private class NoOpDisposable : IDisposable
		{
			public void Dispose()
			{
			}
		}

	}
}
