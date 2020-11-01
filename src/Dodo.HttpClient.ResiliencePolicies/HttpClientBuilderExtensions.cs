using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Dodo.HttpClientResiliencePolicies.CircuitBreakerPolicy;
using Dodo.HttpClientResiliencePolicies.RetryPolicy;
using Dodo.HttpClientResiliencePolicies.TimeoutPolicy;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Registry;
using Polly.Timeout;

namespace Dodo.HttpClientResiliencePolicies
{
	/// <summary>
	/// Extension methods for configuring <see cref="IHttpClientBuilder"/> with Polly retry, timeout, circuit breaker policies.
	/// </summary>
	public static class HttpClientBuilderExtensions
	{
		/// <summary>
		/// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/>
		/// with pre-configured JSON headers, client Timeout and default policies.
		/// </summary>
		/// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
		public static IHttpClientBuilder AddJsonClient<TClientInterface, TClientImplementation>(
			this IServiceCollection sc,
			Uri baseAddress,
			string clientName = null) where TClientInterface : class
			where TClientImplementation : class, TClientInterface
		{
			return AddJsonClient<TClientInterface, TClientImplementation>(
				sc, baseAddress, new ResiliencePoliciesSettings(), clientName);
		}

		/// <summary>
		/// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/>
		/// with pre-configured JSON headers, client Timeout and default policies.
		/// </summary>
		/// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
		public static IHttpClientBuilder AddJsonClient<TClientInterface, TClientImplementation>(
			this IServiceCollection sc,
			Uri baseAddress,
			ResiliencePoliciesSettings settings,
			string clientName = null) where TClientInterface : class
			where TClientImplementation : class, TClientInterface
		{
			var delta = TimeSpan.FromMilliseconds(1000);

			void DefaultClient(HttpClient client)
			{
				client.BaseAddress = baseAddress;
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				client.Timeout = settings.OverallTimeoutPolicySettings.Timeout + delta;
			}

			var httpClientBuilder = string.IsNullOrEmpty(clientName)
				? sc.AddHttpClient<TClientInterface, TClientImplementation>(DefaultClient)
				: sc.AddHttpClient<TClientInterface, TClientImplementation>(clientName, DefaultClient);

			httpClientBuilder
				.AddTimeoutPolicy(settings.OverallTimeoutPolicySettings)
				.AddRetryPolicy(settings.RetrySettings)
				.AddCircuitBreakerPolicy(settings.CircuitBreakerSettings)
				.AddTimeoutPolicy(settings.TimeoutPerTryPolicySettings);

			return httpClientBuilder;
		}

		/// <summary>
		/// Adds pre-configured resilience policies.
		/// </summary>
		/// <param name="clientBuilder">Configured HttpClient builder.</param>
		/// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
		public static IHttpClientBuilder AddResiliencePolicies(
			this IHttpClientBuilder clientBuilder)
		{
			return clientBuilder
				.AddResiliencePolicies(new ResiliencePoliciesSettings());
		}

		/// <summary>
		/// Adds and configures custom resilience policies.
		/// </summary>
		/// <param name="clientBuilder">Configured HttpClient builder.</param>
		/// <param name="settings">Custom resilience policy settings.</param>
		/// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
		public static IHttpClientBuilder AddResiliencePolicies(
			this IHttpClientBuilder clientBuilder,
			ResiliencePoliciesSettings settings)
		{
			return clientBuilder
				.AddTimeoutPolicy(settings.OverallTimeoutPolicySettings)
				.AddRetryPolicy(settings.RetrySettings)
				.AddCircuitBreakerPolicy(settings.CircuitBreakerSettings)
				.AddTimeoutPolicy(settings.TimeoutPerTryPolicySettings);
		}

		private static IHttpClientBuilder AddRetryPolicy(
			this IHttpClientBuilder clientBuilder,
			IRetryPolicySettings settings)
		{
			return clientBuilder
				.AddPolicyHandler(HttpPolicyExtensions
					.HandleTransientHttpError()
					.Or<TimeoutRejectedException>()
					.WaitAndRetryAsync(
						settings.SleepDurationProvider.SleepFunction,
						settings.OnRetry));
		}

		private static IHttpClientBuilder AddCircuitBreakerPolicy(
			this IHttpClientBuilder clientBuilder,
			ICircuitBreakerPolicySettings settings)
		{
			if (settings.IsHostSpecificOn)
			{
				var registry = new PolicyRegistry();
				return clientBuilder.AddPolicyHandler(message =>
				{
					var policyKey = message.RequestUri.Host;
					var policy = registry.GetOrAdd(policyKey, BuildCircuitBreakerPolicy(settings));
					return policy;
				});
			}
			else
			{
				return clientBuilder.AddPolicyHandler(BuildCircuitBreakerPolicy(settings));
			}
		}

		private static AsyncCircuitBreakerPolicy<HttpResponseMessage> BuildCircuitBreakerPolicy(
			ICircuitBreakerPolicySettings settings)
		{
			return HttpPolicyExtensions
				.HandleTransientHttpError()
				.Or<TimeoutRejectedException>()
				.OrResult(r => r.StatusCode == (HttpStatusCode) 429) // Too Many Requests
				.AdvancedCircuitBreakerAsync(
					settings.FailureThreshold,
					settings.SamplingDuration,
					settings.MinimumThroughput,
					settings.DurationOfBreak,
					settings.OnBreak,
					settings.OnReset,
					settings.OnHalfOpen);
		}

		private static IHttpClientBuilder AddTimeoutPolicy(
			this IHttpClientBuilder httpClientBuilder,
			ITimeoutPolicySettings settings)
		{
			return httpClientBuilder.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(settings.Timeout));
		}
	}
}
