using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Dodo.HttpClientResiliencePolicies.CircuitBreakerSettings;
using Dodo.HttpClientResiliencePolicies.RetrySettings;
using Dodo.HttpClientResiliencePolicies.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Dodo.HttpClientResiliencePolicies.Tests.DSL
{
	public sealed class HttpClientWrapperBuilder
	{
		private const string ClientName = "TestClient";
		private readonly Uri _uri = new Uri("http://localhost");
		private readonly Dictionary<string, HttpStatusCode> _hostsResponseCodes = new Dictionary<string, HttpStatusCode>();
		private IRetrySettings _retrySettings;
		private ICircuitBreakerSettings _circuitBreakerSettings;
		private TimeSpan _timeoutPerTry = TimeSpan.FromDays(1);
		private TimeSpan _timeoutOverall = TimeSpan.FromDays(1);
		private TimeSpan _responseLatency = TimeSpan.Zero;
		private int? _retryAfterSeconds = null;
		private DateTime? _retryAfterDate = null;

		public HttpClientWrapperBuilder WithStatusCode(HttpStatusCode statusCode)
		{
			_hostsResponseCodes.Add(string.Empty, statusCode);
			return this;
		}

		public HttpClientWrapperBuilder WithHostAndStatusCode(string host, HttpStatusCode statusCode)
		{
			_hostsResponseCodes.Add(host, statusCode);
			return this;
		}

		public HttpClientWrapperBuilder WithTimeoutOverall(TimeSpan timeoutOverall)
		{
			_timeoutOverall = timeoutOverall;
			return this;
		}

		public HttpClientWrapperBuilder WithTimeoutPerTry(TimeSpan timeoutPerTry)
		{
			_timeoutPerTry = timeoutPerTry;
			return this;
		}

		public HttpClientWrapperBuilder WithRetrySettings(IRetrySettings retrySettings)
		{
			_retrySettings = retrySettings;
			return this;
		}

		public HttpClientWrapperBuilder WithCircuitBreakerSettings(ICircuitBreakerSettings circuitBreakerSettings)
		{
			_circuitBreakerSettings = circuitBreakerSettings;
			return this;
		}

		public HttpClientWrapperBuilder WithRetryAfterHeader(int delaySeconds)
		{
			_retryAfterSeconds = delaySeconds;
			return this;
		}

		public HttpClientWrapperBuilder WithRetryAfterHeader(DateTime date)
		{
			_retryAfterDate = date;
			return this;
		}

		public HttpClientWrapperBuilder WithResponseLatency(TimeSpan responseLatency)
		{
			_responseLatency = responseLatency;
			return this;
		}

		public HttpClientWrapper Please()
		{
			MockHttpMessageHandler handler = CreateMockHttpmessageHandler();
			var settings = BuildClientSettings();

			var services = new ServiceCollection();
			services
				.AddJsonClient<IMockJsonClient, MockJsonClient>(_uri, settings, ClientName)
				.ConfigurePrimaryHttpMessageHandler(() => handler);

			var serviceProvider = services.BuildServiceProvider();
			var factory = serviceProvider.GetService<IHttpClientFactory>();
			var client = factory.CreateClient(ClientName);
			return new HttpClientWrapper(client, handler);
		}

		private MockHttpMessageHandler CreateMockHttpmessageHandler()
		{
			var handler = new MockHttpMessageHandler(_hostsResponseCodes, _responseLatency);

			if (_retryAfterDate != null)
				handler.SetRetryAfterResponseHeader(_retryAfterDate.Value);

			if (_retryAfterSeconds != null)
				handler.SetRetryAfterResponseHeader(_retryAfterSeconds.Value);
			return handler;
		}

		public HttpClientWrapper PleaseHostSpecific()
		{
			MockHttpMessageHandler handler = CreateMockHttpmessageHandler();
			var settings = BuildClientSettings();

			var services = new ServiceCollection();
			services
				.AddJsonClient<IMockJsonClient, MockJsonClient>(_uri, settings, ClientName)
				.AddDefaultHostSpecificPolicies(settings)
				.ConfigurePrimaryHttpMessageHandler(() => handler);

			var serviceProvider = services.BuildServiceProvider();
			var factory = serviceProvider.GetService<IHttpClientFactory>();
			var client = factory.CreateClient(ClientName);
			return new HttpClientWrapper(client, handler);
		}

		private HttpClientSettings BuildClientSettings()
		{
			var defaultCircuitBreakerSettings = _circuitBreakerSettings ?? new CircuitBreakerSettings.CircuitBreakerSettings(
				failureThreshold: 0.5,
				minimumThroughput: int.MaxValue,
				durationOfBreak: TimeSpan.FromMilliseconds(1),
				samplingDuration: TimeSpan.FromMilliseconds(20)
				);

			return new HttpClientSettings(
				timeoutOverall: _timeoutOverall,
				timeoutPerTry: _timeoutPerTry,
				retrySettings: _retrySettings ?? JitterRetrySettings.Default(),
				circuitBreakerSettings: defaultCircuitBreakerSettings);
		}
	}
}
